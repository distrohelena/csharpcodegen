using cs2.core;
using Microsoft.CodeAnalysis;
using System.Runtime.InteropServices;

namespace cs2.cpp;

/// <summary>
/// Lowers managed C# types that appear in P/Invoke signatures to their native ABI representation: fixed-width
/// primitives, enum underlying types, pointers, native mirror structs, and native function-pointer types. Rejects
/// any type whose native representation is not a fixed, unmarshalled bit pattern, reporting the reason and a
/// suggested fix.
/// </summary>
public sealed class CPPPInvokeTypeLowerer {
    /// <summary>
    /// Every mirror struct generated so far, keyed by <see cref="CPPPInvokeMirrorStruct.StructTypeKey"/>, used to
    /// avoid generating duplicate mirrors for the same managed struct type.
    /// </summary>
    Dictionary<string, CPPPInvokeMirrorStruct> MirrorStructsByKey = new Dictionary<string, CPPPInvokeMirrorStruct>();

    /// <summary>
    /// Every mirror struct generated so far, in the order they were generated: nested struct mirrors always appear
    /// before the containers that embed them by value.
    /// </summary>
    List<CPPPInvokeMirrorStruct> MirrorStructList = new List<CPPPInvokeMirrorStruct>();

    /// <summary>
    /// Keys of the structs whose fields are being lowered right now, so a pointer field that points back at an
    /// enclosing struct (for example a linked-list node) does not recurse forever.
    /// </summary>
    HashSet<string> StructsInProgress = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Creates a type lowerer with no mirror structs generated yet.
    /// </summary>
    public CPPPInvokeTypeLowerer() {
    }

    /// <summary>
    /// Gets the layout mirror of every struct whose value or address crossed the boundary so far, de-duplicated by
    /// <see cref="CPPPInvokeMirrorStruct.StructTypeKey"/>, with nested structs listed before the containers that embed
    /// them.
    /// </summary>
    public IReadOnlyList<CPPPInvokeMirrorStruct> MirrorStructs => MirrorStructList;

    /// <summary>
    /// Lowers one parameter's managed type to its native P/Invoke representation, applying the ref/out/in rules for
    /// how the value crosses the boundary.
    /// </summary>
    /// <param name="type">Managed type of the parameter.</param>
    /// <param name="refKind">Managed ref-passing kind of the parameter.</param>
    /// <param name="isCallback">Whether this parameter belongs to a signature invoked by native code as a callback.</param>
    /// <returns>The lowering result: either the lowered type, or the reason the parameter cannot cross the boundary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public CPPPInvokeTypeLoweringResult LowerParameter(ITypeSymbol type, RefKind refKind, bool isCallback) {
        if (type == null) {
            throw new ArgumentNullException(nameof(type));
        }
        if (refKind == RefKind.In) {
            return CPPPInvokeTypeLoweringResult.Failure("in arguments may be rvalues without an address", "use ref or a pointer");
        }

        bool byReference = refKind == RefKind.Ref || refKind == RefKind.Out;
        CPPPInvokeTypeLoweringResult result = LowerValue(type, isCallback, !byReference);
        if (byReference && result.Succeeded) {
            CPPPInvokeValueKind kind = result.Type.Kind;
            if (kind != CPPPInvokeValueKind.Primitive && kind != CPPPInvokeValueKind.Enum && kind != CPPPInvokeValueKind.Struct) {
                return CPPPInvokeTypeLoweringResult.Failure("only blittable values can be passed by ref/out", "use a pointer instead");
            }
        }
        return result;
    }

    /// <summary>
    /// Lowers a method's managed return type to its native P/Invoke representation.
    /// </summary>
    /// <param name="type">Managed return type.</param>
    /// <param name="isCallback">Whether this return type belongs to a signature invoked by native code as a callback.</param>
    /// <returns>The lowering result: either the lowered type, or the reason the return type cannot cross the boundary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public CPPPInvokeTypeLoweringResult LowerReturn(ITypeSymbol type, bool isCallback) {
        if (type == null) {
            throw new ArgumentNullException(nameof(type));
        }
        if (type is IFunctionPointerTypeSymbol) {
            return CPPPInvokeTypeLoweringResult.Failure("function-pointer returns are not supported", "return nint and cast");
        }
        if (type.SpecialType == SpecialType.System_Void) {
            return CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.Void, "void", null));
        }
        return LowerValue(type, isCallback, true);
    }

    /// <summary>
    /// Dispatches a managed type to the lowering rule for its shape: primitive, enum, pointer, function pointer, or
    /// struct, rejecting any type that requires managed marshalling.
    /// </summary>
    /// <param name="type">Managed type to lower.</param>
    /// <param name="isCallback">Whether the value belongs to a signature invoked by native code as a callback.</param>
    /// <param name="byValue">
    /// Whether the value crosses the boundary by value (requiring sequential layout for structs) rather than by
    /// reference or as a field of a struct that crosses by reference.
    /// </param>
    /// <returns>The lowering result for this type.</returns>
    CPPPInvokeTypeLoweringResult LowerValue(ITypeSymbol type, bool isCallback, bool byValue) {
        if (type.SpecialType == SpecialType.System_Void) {
            return CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.Void, "void", null));
        }
        if (type.SpecialType == SpecialType.System_Boolean) {
            return CPPPInvokeTypeLoweringResult.Failure("bool has no fixed native size under DllImport marshalling", "use int (Win32 BOOL) or byte");
        }
        if (type.SpecialType == SpecialType.System_Char) {
            return CPPPInvokeTypeLoweringResult.Failure("char marshals as a 1-byte ANSI character by default", "use ushort for UTF-16 or byte for ANSI");
        }

        string primitiveMirrorText = GetPrimitiveMirrorText(type.SpecialType);
        if (primitiveMirrorText != null) {
            return CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.Primitive, primitiveMirrorText, type));
        }

        if (type.TypeKind == TypeKind.Enum) {
            INamedTypeSymbol enumType = (INamedTypeSymbol)type;
            CPPPInvokeTypeLoweringResult underlying = LowerValue(enumType.EnumUnderlyingType, false, true);
            return CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.Enum, underlying.Type.MirrorTypeText, type));
        }

        if (type is IPointerTypeSymbol pointer) {
            return LowerPointer(pointer);
        }

        if (type is IFunctionPointerTypeSymbol functionPointer) {
            return LowerFunctionPointer(functionPointer);
        }

        if (type.TypeKind == TypeKind.Array
            || type.SpecialType == SpecialType.System_String
            || type.TypeKind == TypeKind.Class
            || type.TypeKind == TypeKind.Interface
            || type.TypeKind == TypeKind.Delegate
            || type.TypeKind == TypeKind.TypeParameter) {
            return RequiresMarshalling();
        }

        if (type.TypeKind == TypeKind.Struct) {
            return LowerStruct((INamedTypeSymbol)type, isCallback, byValue);
        }

        return RequiresMarshalling();
    }

    /// <summary>
    /// Lowers a pointer type. Pointers are never marshalled, so every unmanaged pointee is accepted and the pointer
    /// crosses as <c>void*</c>. When the pointee is a struct, its declaration must still be verifiable (declared in
    /// source, no compiler-generated fields, no <c>StructLayout.Size</c>); a pointee struct whose fields can be mirrored
    /// additionally gets a layout mirror so its generated layout is asserted, while one that cannot be mirrored (for
    /// example because it holds <c>bool</c> or <c>char</c> fields) is accepted without a mirror.
    /// </summary>
    /// <param name="pointer">Pointer type to lower.</param>
    /// <returns>The lowering result for the pointer.</returns>
    CPPPInvokeTypeLoweringResult LowerPointer(IPointerTypeSymbol pointer) {
        CPPPInvokeTypeLoweringResult pointerResult = CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.Pointer, "void*", pointer));
        if (!IsUserStruct(pointer.PointedAtType)) {
            return pointerResult;
        }

        INamedTypeSymbol pointee = (INamedTypeSymbol)pointer.PointedAtType;
        if (StructsInProgress.Contains(pointee.OriginalDefinition.ToDisplayString())) {
            return pointerResult;
        }
        if (TryRejectStructDeclaration(pointee, out CPPPInvokeTypeLoweringResult declarationFailure)) {
            return declarationFailure;
        }

        // The pointee's layout is asserted only when it can be mirrored; a pointee that cannot be mirrored is still a
        // plain unmarshalled pointer, so its lowering failure does not reject the pointer.
        LowerStruct(pointee, false, false);
        return pointerResult;
    }

    /// <summary>
    /// Lowers a struct whose value or address crosses the boundary: rejects unverifiable declarations, generic
    /// structs, fixed-size buffers, callback-by-value structs, non-sequential structs by value and auto-layout structs
    /// by address, recursively lowers every instance field, and registers a layout mirror so the generated struct's
    /// layout is asserted against it.
    /// </summary>
    /// <param name="type">Struct type to lower.</param>
    /// <param name="isCallback">Whether the struct belongs to a signature invoked by native code as a callback.</param>
    /// <param name="byValue">Whether the struct itself crosses the boundary by value (rather than by address).</param>
    /// <returns>The lowering result for the struct.</returns>
    CPPPInvokeTypeLoweringResult LowerStruct(INamedTypeSymbol type, bool isCallback, bool byValue) {
        if (type.IsGenericType) {
            return RequiresMarshalling();
        }
        if (TryRejectStructDeclaration(type, out CPPPInvokeTypeLoweringResult declarationFailure)) {
            return declarationFailure;
        }

        List<IFieldSymbol> fields = GetInstanceFields(type);
        foreach (IFieldSymbol field in fields) {
            if (field.IsFixedSizeBuffer) {
                return CPPPInvokeTypeLoweringResult.Failure("fixed-size buffers are not supported by the C++ backend yet", "use explicit fields");
            }
        }

        if (byValue && isCallback) {
            return CPPPInvokeTypeLoweringResult.Failure("callbacks cannot take structs by value", "take a pointer");
        }

        LayoutKind layoutKind = GetLayoutKind(type);
        if (byValue && layoutKind != LayoutKind.Sequential) {
            return CPPPInvokeTypeLoweringResult.Failure("only sequential structs can cross by value", "pass it by ref or pointer");
        }
        if (layoutKind == LayoutKind.Auto) {
            return CPPPInvokeTypeLoweringResult.Failure("auto-layout structs have no defined native layout", "use LayoutKind.Sequential or LayoutKind.Explicit");
        }

        bool isExplicitLayout = layoutKind == LayoutKind.Explicit;
        string structKey = type.OriginalDefinition.ToDisplayString();
        List<CPPPInvokeMirrorField> mirrorFields = new List<CPPPInvokeMirrorField>();
        StructsInProgress.Add(structKey);
        try {
            foreach (IFieldSymbol field in fields) {
                if (CPPIdentifierSanitizer.SanitizeIdentifier(field.Name) != field.Name) {
                    return CPPPInvokeTypeLoweringResult.Failure($"field '{field.Name}' is a C++ keyword, and the C++ backend does not rename struct fields", "rename the field");
                }
                if (IsUserStruct(field.Type) && GetLayoutKind((INamedTypeSymbol)field.Type) != LayoutKind.Sequential) {
                    return CPPPInvokeTypeLoweringResult.Failure("explicit and auto layout structs cannot be nested inside another struct that crosses the boundary", "declare the overlapping fields directly in the containing struct");
                }

                CPPPInvokeTypeLoweringResult fieldResult = LowerValue(field.Type, false, byValue);
                if (!fieldResult.Succeeded) {
                    return fieldResult;
                }

                mirrorFields.Add(isExplicitLayout
                    ? new CPPPInvokeMirrorField(field.Name, fieldResult.Type.MirrorTypeText, GetFieldOffset(field))
                    : new CPPPInvokeMirrorField(field.Name, fieldResult.Type.MirrorTypeText));
            }
        } finally {
            StructsInProgress.Remove(structKey);
        }

        string mirrorName = CreateMirrorName(type);
        RegisterMirror(new CPPPInvokeMirrorStruct(mirrorName, type, GetLayoutPack(type), isExplicitLayout, mirrorFields));
        return CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.Struct, mirrorName, type));
    }

    /// <summary>
    /// Rejects a struct declaration whose generated layout cannot be verified wherever the struct crosses the boundary
    /// (by value, by address, or as a pointee): structs that are not declared in source, structs with
    /// compiler-generated backing fields (auto-properties and record struct parameters), and structs that set
    /// <c>StructLayout.Size</c>.
    /// </summary>
    /// <param name="type">Struct type to check.</param>
    /// <param name="failure">The rejection when this method returns <c>true</c>; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if the declaration is rejected; otherwise <c>false</c>.</returns>
    static bool TryRejectStructDeclaration(INamedTypeSymbol type, out CPPPInvokeTypeLoweringResult failure) {
        if (type.OriginalDefinition.DeclaringSyntaxReferences.Length == 0) {
            failure = CPPPInvokeTypeLoweringResult.Failure("struct is not declared in source; its generated layout cannot be verified", "declare an equivalent struct in source");
            return true;
        }
        if (GetInstanceFields(type).Any(field => field.AssociatedSymbol is IPropertySymbol || field.IsImplicitlyDeclared)) {
            failure = CPPPInvokeTypeLoweringResult.Failure("auto-properties and record struct parameters generate hidden fields", "declare explicit fields");
            return true;
        }

        AttributeData layoutAttribute = GetStructLayoutAttribute(type);
        if (layoutAttribute != null && layoutAttribute.NamedArguments.Any(namedArgument => namedArgument.Key == "Size" && Convert.ToInt32(namedArgument.Value.Value) > 0)) {
            failure = CPPPInvokeTypeLoweringResult.UnsupportedSetting("StructLayout.Size is not supported", "remove Size and declare explicit padding fields");
            return true;
        }

        failure = null;
        return false;
    }

    /// <summary>
    /// Determines whether a type is a user struct (a non-enum value type that is not a lowerable primitive,
    /// <c>void</c>, <c>bool</c>, or <c>char</c>), whose layout the P/Invoke rules must verify.
    /// </summary>
    /// <param name="type">Type to classify.</param>
    /// <returns><c>true</c> if the type is a user struct; otherwise <c>false</c>.</returns>
    static bool IsUserStruct(ITypeSymbol type) {
        return type.TypeKind == TypeKind.Struct
            && type is INamedTypeSymbol
            && GetPrimitiveMirrorText(type.SpecialType) == null
            && type.SpecialType != SpecialType.System_Void
            && type.SpecialType != SpecialType.System_Boolean
            && type.SpecialType != SpecialType.System_Char;
    }

    /// <summary>
    /// Gets every instance field of a struct in declaration order, including compiler-generated backing fields.
    /// </summary>
    /// <param name="type">Struct whose fields are listed.</param>
    /// <returns>The non-static, non-const fields of the struct.</returns>
    static List<IFieldSymbol> GetInstanceFields(INamedTypeSymbol type) {
        return type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic && !field.IsConst).ToList();
    }

    /// <summary>
    /// Finds the <c>StructLayoutAttribute</c> applied to a struct.
    /// </summary>
    /// <param name="type">Struct whose attributes are searched.</param>
    /// <returns>The attribute, or <c>null</c> when the struct uses the default sequential layout.</returns>
    static AttributeData GetStructLayoutAttribute(INamedTypeSymbol type) {
        return type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
    }

    /// <summary>
    /// Reads a struct's layout kind, which is sequential unless a <c>StructLayoutAttribute</c> says otherwise.
    /// </summary>
    /// <param name="type">Struct whose layout kind is read.</param>
    /// <returns>The struct's layout kind.</returns>
    static LayoutKind GetLayoutKind(INamedTypeSymbol type) {
        AttributeData layoutAttribute = GetStructLayoutAttribute(type);
        return layoutAttribute == null ? LayoutKind.Sequential : (LayoutKind)Convert.ToInt32(layoutAttribute.ConstructorArguments[0].Value);
    }

    /// <summary>
    /// Reads a struct's explicit <c>StructLayout.Pack</c>.
    /// </summary>
    /// <param name="type">Struct whose packing is read.</param>
    /// <returns>The packing in bytes, or <c>0</c> for the platform default.</returns>
    static int GetLayoutPack(INamedTypeSymbol type) {
        AttributeData layoutAttribute = GetStructLayoutAttribute(type);
        if (layoutAttribute == null) {
            return 0;
        }
        foreach (KeyValuePair<string, TypedConstant> namedArgument in layoutAttribute.NamedArguments) {
            if (namedArgument.Key == "Pack") {
                return Convert.ToInt32(namedArgument.Value.Value);
            }
        }
        return 0;
    }

    /// <summary>
    /// Reads the <c>FieldOffset</c> of a field of an explicit-layout struct.
    /// </summary>
    /// <param name="field">Field whose offset is read.</param>
    /// <returns>The field's offset in bytes.</returns>
    /// <exception cref="InvalidOperationException">The field has no <c>FieldOffsetAttribute</c>.</exception>
    static int GetFieldOffset(IFieldSymbol field) {
        AttributeData offsetAttribute = field.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass.ToDisplayString() == "System.Runtime.InteropServices.FieldOffsetAttribute");
        if (offsetAttribute == null) {
            throw new InvalidOperationException($"Field '{field.ToDisplayString()}' of an explicit-layout struct has no FieldOffset attribute.");
        }
        return Convert.ToInt32(offsetAttribute.ConstructorArguments[0].Value);
    }

    /// <summary>
    /// Lowers a function-pointer type to its native calling-convention-qualified function-pointer text, rejecting
    /// managed function pointers and unsupported calling conventions.
    /// </summary>
    /// <param name="functionPointer">Function-pointer type to lower.</param>
    /// <returns>The lowering result for the function pointer.</returns>
    CPPPInvokeTypeLoweringResult LowerFunctionPointer(IFunctionPointerTypeSymbol functionPointer) {
        IMethodSymbol signature = functionPointer.Signature;
        CPPPInvokeCallingConvention callingConvention;
        switch (UnmanagedCallingConventionResolver.Resolve(signature)) {
            case UnmanagedCallingConventionKind.StdCall:
                callingConvention = CPPPInvokeCallingConvention.StdCall;
                break;
            case UnmanagedCallingConventionKind.Cdecl:
                callingConvention = CPPPInvokeCallingConvention.Cdecl;
                break;
            case UnmanagedCallingConventionKind.Managed:
                return CPPPInvokeTypeLoweringResult.Failure("managed function pointers cannot be called from native code", "use delegate* unmanaged[Stdcall]");
            default:
                return CPPPInvokeTypeLoweringResult.Failure("unsupported calling convention", "use delegate* unmanaged[Stdcall] or delegate* unmanaged[Cdecl]");
        }

        CPPPInvokeTypeLoweringResult returnResult = LowerReturn(signature.ReturnType, true);
        if (!returnResult.Succeeded) {
            return returnResult;
        }

        List<string> parameterTexts = new List<string>();
        foreach (IParameterSymbol parameter in signature.Parameters) {
            CPPPInvokeTypeLoweringResult parameterResult = LowerParameter(parameter.Type, parameter.RefKind, true);
            if (!parameterResult.Succeeded) {
                return parameterResult;
            }
            parameterTexts.Add(parameter.RefKind != RefKind.None ? "void*" : parameterResult.Type.MirrorTypeText);
        }

        string macro = callingConvention == CPPPInvokeCallingConvention.StdCall ? "HE_CPP_STDCALL" : "HE_CPP_CDECL";
        string mirrorTypeText = returnResult.Type.MirrorTypeText + " (" + macro + "*)(" + string.Join(",", parameterTexts) + ")";
        return CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.FunctionPointer, mirrorTypeText, functionPointer));
    }

    /// <summary>
    /// Registers a newly lowered struct's layout mirror, skipping registration when a mirror for the same managed
    /// struct type was already generated.
    /// </summary>
    /// <param name="mirror">Layout mirror of the lowered struct.</param>
    void RegisterMirror(CPPPInvokeMirrorStruct mirror) {
        if (MirrorStructsByKey.ContainsKey(mirror.StructTypeKey)) {
            return;
        }
        MirrorStructsByKey.Add(mirror.StructTypeKey, mirror);
        MirrorStructList.Add(mirror);
    }

    /// <summary>
    /// Maps a blittable primitive's <see cref="SpecialType"/> to its fixed-width native mirror type text.
    /// </summary>
    /// <param name="specialType">Special type of the candidate primitive.</param>
    /// <returns>The native mirror type text, or <c>null</c> if the special type is not a lowerable primitive.</returns>
    static string GetPrimitiveMirrorText(SpecialType specialType) {
        switch (specialType) {
            case SpecialType.System_SByte: return "int8_t";
            case SpecialType.System_Byte: return "uint8_t";
            case SpecialType.System_Int16: return "int16_t";
            case SpecialType.System_UInt16: return "uint16_t";
            case SpecialType.System_Int32: return "int32_t";
            case SpecialType.System_UInt32: return "uint32_t";
            case SpecialType.System_Int64: return "int64_t";
            case SpecialType.System_UInt64: return "uint64_t";
            case SpecialType.System_Single: return "float";
            case SpecialType.System_Double: return "double";
            case SpecialType.System_IntPtr: return "intptr_t";
            case SpecialType.System_UIntPtr: return "uintptr_t";
            default: return null;
        }
    }

    /// <summary>
    /// Builds the failure result shared by every type that cannot be lowered without managed marshalling (strings,
    /// arrays, classes, interfaces, delegates, generic structs, and type parameters).
    /// </summary>
    /// <returns>A failure result recommending a pointer be passed instead.</returns>
    static CPPPInvokeTypeLoweringResult RequiresMarshalling() {
        return CPPPInvokeTypeLoweringResult.Failure("requires marshalling", "pass a pointer (byte*/ushort*/T*) instead");
    }

    /// <summary>
    /// Builds the generated native mirror struct name for a managed struct type: <c>he_pinvoke_</c> followed by the
    /// type's display name with every character outside <c>[A-Za-z0-9_]</c> replaced by <c>_</c>.
    /// </summary>
    /// <param name="type">Managed struct type to name a mirror for.</param>
    /// <returns>The generated mirror struct name.</returns>
    static string CreateMirrorName(INamedTypeSymbol type) {
        string displayName = type.ToDisplayString();
        char[] sanitized = displayName.ToCharArray();
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
        return "he_pinvoke_" + new string(sanitized);
    }
}
