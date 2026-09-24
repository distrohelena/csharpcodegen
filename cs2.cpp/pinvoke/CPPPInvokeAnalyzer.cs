using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;

namespace cs2.cpp;

/// <summary>
/// Walks every method reachable from the global namespace of a set of compilations, validates each P/Invoke-shaped
/// declaration (<c>DllImport</c>, <c>LibraryImport</c>, or <c>UnmanagedCallersOnly</c>), lowers its signature to a
/// native ABI representation, and assembles the accepted declarations into a <see cref="CPPPInvokePlan"/>. Every
/// declaration that cannot be safely lowered is rejected with a stable <c>CPPPINV</c> diagnostic instead of being
/// silently skipped or partially emitted.
/// </summary>
public sealed class CPPPInvokeAnalyzer {
    /// <summary>
    /// Fully qualified display name of <c>LibraryImportAttribute</c>, which direct lowering does not support.
    /// </summary>
    const string LibraryImportAttributeName = "System.Runtime.InteropServices.LibraryImportAttribute";

    /// <summary>
    /// Fully qualified display name of <c>MarshalAsAttribute</c>; its presence on a DllImport parameter or return
    /// means the signature relies on managed marshalling that direct lowering does not implement.
    /// </summary>
    const string MarshalAsAttributeName = "System.Runtime.InteropServices.MarshalAsAttribute";

    /// <summary>
    /// Fully qualified display name of <c>UnmanagedCallersOnlyAttribute</c>, which marks a managed method as a
    /// native callback target.
    /// </summary>
    const string UnmanagedCallersOnlyAttributeName = "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute";

    /// <summary>
    /// Fully qualified display name of the <c>CallConvStdcall</c> marker type.
    /// </summary>
    const string CallConvStdcallName = "System.Runtime.CompilerServices.CallConvStdcall";

    /// <summary>
    /// Fully qualified display name of the <c>CallConvCdecl</c> marker type.
    /// </summary>
    const string CallConvCdeclName = "System.Runtime.CompilerServices.CallConvCdecl";

    /// <summary>
    /// Diagnostic factory used to build source-located hard errors for every rejected declaration.
    /// </summary>
    readonly CPPOwnershipDiagnosticFactory DiagnosticFactory = new CPPOwnershipDiagnosticFactory();

    /// <summary>
    /// Analyzes every method reachable from the global namespace of each compilation, validates its P/Invoke shape,
    /// lowers its signature, and assembles the accepted declarations into a validated plan.
    /// </summary>
    /// <param name="compilations">Every compilation whose P/Invoke declarations should be analyzed together, so
    /// entry points shared across projects are de-duplicated and cross-checked consistently.</param>
    /// <returns>The validated plan together with every diagnostic raised while building it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="compilations"/> is null.</exception>
    public CPPPInvokeAnalysisResult Analyze(IReadOnlyList<Compilation> compilations) {
        if (compilations == null) {
            throw new ArgumentNullException(nameof(compilations));
        }

        List<IMethodSymbol> methods = new List<IMethodSymbol>();
        foreach (Compilation compilation in compilations) {
            CollectMethods(compilation.SourceModule.GlobalNamespace, methods);
        }

        Dictionary<string, int> callbackNameCountsByType = CountCallbackNamesByType(methods);
        List<CPPConversionDiagnostic> diagnostics = new List<CPPConversionDiagnostic>();
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Dictionary<string, CPPPInvokeImport> importsByEntryPoint = new Dictionary<string, CPPPInvokeImport>(StringComparer.Ordinal);
        List<CPPPInvokeImport> imports = new List<CPPPInvokeImport>();
        List<CPPPInvokeCallback> callbacks = new List<CPPPInvokeCallback>();

        foreach (IMethodSymbol method in methods) {
            if (HasAttribute(method, LibraryImportAttributeName)) {
                diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.LibraryImportNotSupported,
                    "LibraryImport is not supported; use DllImport with a blittable signature.",
                    "Replace [LibraryImport] with [DllImport] and a blittable signature."));
                continue;
            }

            DllImportData dllImportData = method.GetDllImportData();
            if (dllImportData != null) {
                AnalyzeImport(method, dllImportData, lowerer, importsByEntryPoint, imports, diagnostics);
                continue;
            }

            if (HasAttribute(method, UnmanagedCallersOnlyAttributeName)) {
                AnalyzeCallback(method, lowerer, callbackNameCountsByType, callbacks, diagnostics);
            }
        }

        imports.Sort((left, right) => {
            int libraryComparison = string.CompareOrdinal(left.LibraryNamespace, right.LibraryNamespace);
            return libraryComparison != 0 ? libraryComparison : string.CompareOrdinal(left.EntryPoint, right.EntryPoint);
        });
        callbacks.Sort((left, right) => string.CompareOrdinal(left.TrampolineName, right.TrampolineName));

        CPPPInvokePlan plan = new CPPPInvokePlan(imports, callbacks, lowerer.MirrorStructs);
        return new CPPPInvokeAnalysisResult(plan, diagnostics);
    }

    /// <summary>
    /// Recursively collects every method symbol declared in a namespace or type, walking into nested namespaces and
    /// nested types.
    /// </summary>
    /// <param name="container">Namespace or type whose members are collected.</param>
    /// <param name="methods">List every discovered method symbol is appended to.</param>
    static void CollectMethods(INamespaceOrTypeSymbol container, List<IMethodSymbol> methods) {
        foreach (ISymbol member in container.GetMembers()) {
            if (member is IMethodSymbol method) {
                methods.Add(method);
            }
            if (member is INamespaceOrTypeSymbol nested) {
                CollectMethods(nested, methods);
            }
        }
    }

    /// <summary>
    /// Counts, per containing type, how many UnmanagedCallersOnly methods share each method name, so a duplicate can
    /// be rejected regardless of which declaration is visited first.
    /// </summary>
    /// <param name="methods">Every method symbol collected from the analyzed compilations.</param>
    /// <returns>A map from "containing type::method name" to the number of UnmanagedCallersOnly methods sharing it.</returns>
    static Dictionary<string, int> CountCallbackNamesByType(List<IMethodSymbol> methods) {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IMethodSymbol method in methods) {
            if (!HasAttribute(method, UnmanagedCallersOnlyAttributeName)) {
                continue;
            }
            string key = BuildCallbackNameCountKey(method.ContainingType, method.Name);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }
        return counts;
    }

    /// <summary>
    /// Builds the key used to count same-named UnmanagedCallersOnly methods within one containing type.
    /// </summary>
    /// <param name="containingType">Containing type of the callback method.</param>
    /// <param name="methodName">Name of the callback method.</param>
    /// <returns>A stable key combining the containing type and method name.</returns>
    static string BuildCallbackNameCountKey(INamedTypeSymbol containingType, string methodName) {
        return containingType.ToDisplayString() + "::" + methodName;
    }

    /// <summary>
    /// Validates one DllImport method's settings, calling convention, and lowered signature, then merges it into the
    /// import table by entry point, or reports the diagnostics that reject it.
    /// </summary>
    /// <param name="method">DllImport method being analyzed.</param>
    /// <param name="dllImportData">The method's DllImport metadata.</param>
    /// <param name="lowerer">Shared type lowerer used so mirror structs are shared across the whole plan.</param>
    /// <param name="importsByEntryPoint">Accepted imports indexed by entry point, used for de-duplication.</param>
    /// <param name="imports">List every newly accepted import is appended to.</param>
    /// <param name="diagnostics">List every rejection diagnostic is appended to.</param>
    void AnalyzeImport(IMethodSymbol method, DllImportData dllImportData, CPPPInvokeTypeLowerer lowerer,
        Dictionary<string, CPPPInvokeImport> importsByEntryPoint, List<CPPPInvokeImport> imports, List<CPPConversionDiagnostic> diagnostics) {
        bool hasError = false;

        if (dllImportData.SetLastError == true) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.UnsupportedImportSetting,
                "SetLastError is not supported by direct P/Invoke lowering.",
                "Remove SetLastError; direct forwarders do not marshal the last Win32 error."));
            hasError = true;
        }
        if (dllImportData.CharacterSet == CharSet.Unicode || dllImportData.CharacterSet == CharSet.Auto) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.UnsupportedImportSetting,
                "CharSet.Unicode and CharSet.Auto marshalling are not supported by direct P/Invoke lowering.",
                "Use explicit byte or ushort buffers instead of managed string marshalling."));
            hasError = true;
        }
        if (dllImportData.BestFitMapping == true) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.UnsupportedImportSetting,
                "BestFitMapping is not supported by direct P/Invoke lowering.",
                "Remove BestFitMapping; it only affects managed string marshalling."));
            hasError = true;
        }
        if (dllImportData.ThrowOnUnmappableCharacter == true) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.UnsupportedImportSetting,
                "ThrowOnUnmappableCharacter is not supported by direct P/Invoke lowering.",
                "Remove ThrowOnUnmappableCharacter; it only affects managed string marshalling."));
            hasError = true;
        }
        if ((method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) == 0) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.UnsupportedImportSetting,
                "PreserveSig=false is not supported; direct P/Invoke lowering does not convert a returned HRESULT into an exception.",
                "Set PreserveSig to true (the default) and inspect the returned HRESULT explicitly."));
            hasError = true;
        }
        if (HasMarshalAsAttribute(method)) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.UnsupportedImportSetting,
                "MarshalAs attributes are not supported by direct P/Invoke lowering.",
                "Remove MarshalAs and use a blittable type instead."));
            hasError = true;
        }

        if (!TryResolveCallingConvention(dllImportData.CallingConvention, out CPPPInvokeCallingConvention callingConvention)) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.UnsupportedCallingConvention,
                $"Calling convention '{dllImportData.CallingConvention}' is not supported by direct P/Invoke lowering.",
                "Use CallingConvention.Winapi, StdCall, or Cdecl."));
            hasError = true;
        }

        List<CPPPInvokeParameter> parameters = new List<CPPPInvokeParameter>();
        bool signatureLowered = TryLowerSignature(method, lowerer, false, CPPPInvokeDiagnosticCodes.UnsupportedType,
            diagnostics, out CPPPInvokeTypeLoweringResult returnResult, parameters);
        if (!signatureLowered) {
            hasError = true;
        }

        if (hasError) {
            return;
        }

        string entryPoint = string.IsNullOrEmpty(dllImportData.EntryPointName) ? method.Name : dllImportData.EntryPointName;
        string library = CPPPInvokeLibraryNameNormalizer.Normalize(dllImportData.ModuleName);
        string methodId = CPPPInvokeMethodIds.Get(method);
        CPPPInvokeSignature signature = new CPPPInvokeSignature(returnResult.Type, parameters, callingConvention);

        if (importsByEntryPoint.TryGetValue(entryPoint, out CPPPInvokeImport existingImport)) {
            if (existingImport.LibraryNamespace != library) {
                diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.SymbolImportedFromMultipleLibraries,
                    $"Entry point '{entryPoint}' is imported from multiple libraries ('{existingImport.LibraryNamespace}' and '{library}').",
                    "Use distinct entry point names, or import the symbol from a single library."));
                return;
            }
            if (existingImport.Signature.MirrorKey != signature.MirrorKey) {
                diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.ConflictingImportSignature,
                    $"Entry point '{entryPoint}' in library '{library}' is already imported with an incompatible signature.",
                    "Give every DllImport declaration for this entry point identical parameter and return types."));
                return;
            }
            existingImport.MethodIds.Add(methodId);
            return;
        }

        CPPPInvokeImport import = new CPPPInvokeImport(library, entryPoint, signature);
        import.MethodIds.Add(methodId);
        importsByEntryPoint.Add(entryPoint, import);
        imports.Add(import);
    }

    /// <summary>
    /// Validates one UnmanagedCallersOnly method's shape, calling convention, and lowered signature, then adds its
    /// trampoline to the callback table, or reports the diagnostics that reject it.
    /// </summary>
    /// <param name="method">UnmanagedCallersOnly method being analyzed.</param>
    /// <param name="lowerer">Shared type lowerer used so mirror structs are shared across the whole plan.</param>
    /// <param name="callbackNameCountsByType">Duplicate-name counts computed up front across every compilation.</param>
    /// <param name="callbacks">List every newly accepted trampoline is appended to.</param>
    /// <param name="diagnostics">List every rejection diagnostic is appended to.</param>
    void AnalyzeCallback(IMethodSymbol method, CPPPInvokeTypeLowerer lowerer, Dictionary<string, int> callbackNameCountsByType,
        List<CPPPInvokeCallback> callbacks, List<CPPConversionDiagnostic> diagnostics) {
        bool hasError = false;
        AttributeData attribute = method.GetAttributes().First(candidate => candidate.AttributeClass.ToDisplayString() == UnmanagedCallersOnlyAttributeName);

        if (!method.IsStatic) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.InvalidCallbackMethod,
                "UnmanagedCallersOnly methods must be static.",
                "Make the method static."));
            hasError = true;
        }
        if (method.IsGenericMethod) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.InvalidCallbackMethod,
                "UnmanagedCallersOnly methods cannot be generic.",
                "Remove the method's generic type parameters."));
            hasError = true;
        }
        if (attribute.NamedArguments.Any(namedArgument => namedArgument.Key == "EntryPoint")) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.InvalidCallbackMethod,
                "UnmanagedCallersOnly does not support a custom EntryPoint; the trampoline name is generated from the method's qualified name.",
                "Remove the EntryPoint named argument."));
            hasError = true;
        }
        if (callbackNameCountsByType[BuildCallbackNameCountKey(method.ContainingType, method.Name)] > 1) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.InvalidCallbackMethod,
                $"Another UnmanagedCallersOnly method named '{method.Name}' already exists on '{method.ContainingType.ToDisplayString()}'.",
                "Give each UnmanagedCallersOnly method in the type a unique name."));
            hasError = true;
        }

        if (!TryResolveCallbackCallingConvention(attribute, out CPPPInvokeCallingConvention callingConvention)) {
            diagnostics.Add(CreateDiagnostic(method, CPPPInvokeDiagnosticCodes.UnsupportedCallingConvention,
                "CallConvs must specify exactly one of CallConvStdcall or CallConvCdecl.",
                "Set CallConvs = new[] { typeof(CallConvStdcall) } or new[] { typeof(CallConvCdecl) }."));
            hasError = true;
        }

        List<CPPPInvokeParameter> parameters = new List<CPPPInvokeParameter>();
        bool signatureLowered = TryLowerSignature(method, lowerer, true, CPPPInvokeDiagnosticCodes.UnsupportedCallbackType,
            diagnostics, out CPPPInvokeTypeLoweringResult returnResult, parameters);
        if (!signatureLowered) {
            hasError = true;
        }

        if (hasError) {
            return;
        }

        CPPPInvokeSignature signature = new CPPPInvokeSignature(returnResult.Type, parameters, callingConvention);
        string trampolineName = "he_pinvoke_cb_" + Sanitize(method.ContainingType.ToDisplayString()) + "_" + method.Name;
        callbacks.Add(new CPPPInvokeCallback(CPPPInvokeMethodIds.Get(method), trampolineName, signature));
    }

    /// <summary>
    /// Lowers a method's return type and every parameter with the shared type lowerer, reporting <paramref
    /// name="typeErrorCode"/> for the return type and for every parameter that fails to lower (not just the
    /// first). Shared between DllImport and UnmanagedCallersOnly analysis, which differ only in whether the
    /// signature is lowered for a callback and which diagnostic code a failure reports.
    /// </summary>
    /// <param name="method">Method whose return type and parameters are lowered.</param>
    /// <param name="lowerer">Shared type lowerer used so mirror structs are shared across the whole plan.</param>
    /// <param name="isCallback">Whether the signature belongs to a method invoked by native code as a callback.</param>
    /// <param name="typeErrorCode">Diagnostic code to report for a failing return type or parameter: <c>CPPPINV002</c>
    /// for DllImport signatures, <c>CPPPINV008</c> for callback signatures.</param>
    /// <param name="diagnostics">List every failure diagnostic is appended to.</param>
    /// <param name="returnResult">The lowered return type result, whether it succeeded or failed.</param>
    /// <param name="parameters">List every successfully lowered parameter is appended to, in declaration order.</param>
    /// <returns><c>true</c> if the return type and every parameter lowered successfully; otherwise <c>false</c>.</returns>
    bool TryLowerSignature(IMethodSymbol method, CPPPInvokeTypeLowerer lowerer, bool isCallback, string typeErrorCode,
        List<CPPConversionDiagnostic> diagnostics, out CPPPInvokeTypeLoweringResult returnResult, List<CPPPInvokeParameter> parameters) {
        bool succeeded = true;

        returnResult = lowerer.LowerReturn(method.ReturnType, isCallback);
        if (!returnResult.Succeeded) {
            diagnostics.Add(CreateDiagnostic(method, typeErrorCode,
                BuildTypeFailureMessage("Return type", returnResult), returnResult.Recommendation));
            succeeded = false;
        }

        foreach (IParameterSymbol parameter in method.Parameters) {
            CPPPInvokeTypeLoweringResult parameterResult = lowerer.LowerParameter(parameter.Type, parameter.RefKind, isCallback);
            if (!parameterResult.Succeeded) {
                diagnostics.Add(CreateDiagnostic(method, typeErrorCode,
                    BuildTypeFailureMessage($"Parameter '{parameter.Name}'", parameterResult), parameterResult.Recommendation));
                succeeded = false;
                continue;
            }
            parameters.Add(new CPPPInvokeParameter(parameter.Name, parameterResult.Type, parameter.RefKind));
        }

        return succeeded;
    }

    /// <summary>
    /// Determines whether a method carries an attribute whose class matches the given fully qualified display name.
    /// </summary>
    /// <param name="method">Method whose attributes are inspected.</param>
    /// <param name="attributeDisplayName">Fully qualified display name to match.</param>
    /// <returns><c>true</c> if a matching attribute is present; otherwise <c>false</c>.</returns>
    static bool HasAttribute(IMethodSymbol method, string attributeDisplayName) {
        return method.GetAttributes().Any(attribute => attribute.AttributeClass.ToDisplayString() == attributeDisplayName);
    }

    /// <summary>
    /// Determines whether a DllImport method has a <c>MarshalAsAttribute</c> on any parameter or on its return
    /// value, either of which means the signature depends on managed marshalling.
    /// </summary>
    /// <param name="method">DllImport method being checked.</param>
    /// <returns><c>true</c> if a <c>MarshalAsAttribute</c> is present anywhere in the signature; otherwise <c>false</c>.</returns>
    static bool HasMarshalAsAttribute(IMethodSymbol method) {
        if (method.GetReturnTypeAttributes().Any(attribute => attribute.AttributeClass.ToDisplayString() == MarshalAsAttributeName)) {
            return true;
        }
        foreach (IParameterSymbol parameter in method.Parameters) {
            if (parameter.GetAttributes().Any(attribute => attribute.AttributeClass.ToDisplayString() == MarshalAsAttributeName)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Resolves a DllImport calling convention to one of the two native conventions direct lowering can forward.
    /// </summary>
    /// <param name="callingConvention">Calling convention declared on the DllImport attribute.</param>
    /// <param name="resolved">The resolved native calling convention when this method returns <c>true</c>.</param>
    /// <returns><c>true</c> if the calling convention is supported; otherwise <c>false</c>.</returns>
    static bool TryResolveCallingConvention(CallingConvention callingConvention, out CPPPInvokeCallingConvention resolved) {
        switch (callingConvention) {
            case CallingConvention.Winapi:
            case CallingConvention.StdCall:
                resolved = CPPPInvokeCallingConvention.StdCall;
                return true;
            case CallingConvention.Cdecl:
                resolved = CPPPInvokeCallingConvention.Cdecl;
                return true;
            default:
                resolved = CPPPInvokeCallingConvention.StdCall;
                return false;
        }
    }

    /// <summary>
    /// Resolves an UnmanagedCallersOnly attribute's <c>CallConvs</c> named argument to one of the two native
    /// conventions direct lowering can forward. A missing or empty array resolves to StdCall.
    /// </summary>
    /// <param name="attribute">The UnmanagedCallersOnly attribute instance.</param>
    /// <param name="resolved">The resolved native calling convention when this method returns <c>true</c>.</param>
    /// <returns><c>true</c> if the calling convention is supported; otherwise <c>false</c>.</returns>
    static bool TryResolveCallbackCallingConvention(AttributeData attribute, out CPPPInvokeCallingConvention resolved) {
        resolved = CPPPInvokeCallingConvention.StdCall;
        foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments) {
            if (namedArgument.Key != "CallConvs") {
                continue;
            }
            ImmutableArray<TypedConstant> values = namedArgument.Value.Values;
            if (values.IsDefaultOrEmpty) {
                return true;
            }
            if (values.Length > 1) {
                return false;
            }
            INamedTypeSymbol conventionType = (INamedTypeSymbol)values[0].Value;
            string conventionName = conventionType.ToDisplayString();
            if (conventionName == CallConvStdcallName) {
                resolved = CPPPInvokeCallingConvention.StdCall;
                return true;
            }
            if (conventionName == CallConvCdeclName) {
                resolved = CPPPInvokeCallingConvention.Cdecl;
                return true;
            }
            return false;
        }
        return true;
    }

    /// <summary>
    /// Builds the CPPPINV002/CPPPINV008 message naming the failing parameter or return value and including the
    /// lowerer's failure reason.
    /// </summary>
    /// <param name="label">Either <c>"Return type"</c> or a quoted parameter name.</param>
    /// <param name="result">The failed lowering result.</param>
    /// <returns>The composed diagnostic message.</returns>
    static string BuildTypeFailureMessage(string label, CPPPInvokeTypeLoweringResult result) {
        return $"{label} cannot cross the P/Invoke boundary: {result.FailureReason}.";
    }

    /// <summary>
    /// Replaces every character outside <c>[A-Za-z0-9_]</c> with <c>_</c>, used to turn a qualified type name into a
    /// valid trampoline name segment.
    /// </summary>
    /// <param name="text">Text to sanitize.</param>
    /// <returns>The sanitized text.</returns>
    static string Sanitize(string text) {
        char[] sanitized = text.ToCharArray();
        for (int index = 0; index < sanitized.Length; index++) {
            char character = sanitized[index];
            bool isIdentifierCharacter = (character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '_';
            if (!isIdentifierCharacter) {
                sanitized[index] = '_';
            }
        }
        return new string(sanitized);
    }

    /// <summary>
    /// Builds a source-located hard-error diagnostic for a rejected method declaration.
    /// </summary>
    /// <param name="method">Method the diagnostic applies to.</param>
    /// <param name="code">Stable CPPPINV diagnostic code.</param>
    /// <param name="message">Explanation of the rejection.</param>
    /// <param name="recommendation">Concrete source correction that would resolve the rejection.</param>
    /// <returns>The composed diagnostic.</returns>
    CPPConversionDiagnostic CreateDiagnostic(IMethodSymbol method, string code, string message, string recommendation) {
        SyntaxNode node = method.DeclaringSyntaxReferences[0].GetSyntax();
        return DiagnosticFactory.Create(code, node, method, message, recommendation);
    }
}
