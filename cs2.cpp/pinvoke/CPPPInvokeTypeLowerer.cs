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
    /// Creates a type lowerer with no mirror structs generated yet.
    /// </summary>
    public CPPPInvokeTypeLowerer() {
    }

    /// <summary>
    /// Gets every struct lowered by value so far, de-duplicated by <see cref="CPPPInvokeMirrorStruct.StructTypeKey"/>,
    /// with nested structs listed before the containers that embed them.
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
    /// Whether the value crosses the boundary by value (registering struct mirrors and requiring sequential layout)
    /// rather than by reference, pointer, or as a struct field validated only for blittability.
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
            if (pointer.PointedAtType.SpecialType != SpecialType.System_Void) {
                CPPPInvokeTypeLoweringResult pointee = LowerValue(pointer.PointedAtType, false, false);
                if (!pointee.Succeeded) {
                    return pointee;
                }
            }
            return CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.Pointer, "void*", type));
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
    /// Lowers a struct type: rejects generic structs and fixed-size buffers outright, rejects non-sequential layout
    /// and callback-by-value when crossing by value, recursively lowers every instance field, and registers a mirror
    /// struct when the struct crosses the boundary by value.
    /// </summary>
    /// <param name="type">Struct type to lower.</param>
    /// <param name="isCallback">Whether the struct belongs to a signature invoked by native code as a callback.</param>
    /// <param name="byValue">Whether the struct itself crosses the boundary by value.</param>
    /// <returns>The lowering result for the struct.</returns>
    CPPPInvokeTypeLoweringResult LowerStruct(INamedTypeSymbol type, bool isCallback, bool byValue) {
        if (type.IsGenericType) {
            return RequiresMarshalling();
        }

        List<IFieldSymbol> fields = type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic && !field.IsConst).ToList();
        foreach (IFieldSymbol field in fields) {
            if (field.IsFixedSizeBuffer) {
                return CPPPInvokeTypeLoweringResult.Failure("fixed-size buffers are not supported by the C++ backend yet", "use explicit fields");
            }
        }

        if (byValue && isCallback) {
            return CPPPInvokeTypeLoweringResult.Failure("callbacks cannot take structs by value", "take a pointer");
        }

        AttributeData layoutAttribute = type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
        LayoutKind layoutKind = LayoutKind.Sequential;
        int pack = 0;
        if (layoutAttribute != null) {
            layoutKind = (LayoutKind)Convert.ToInt32(layoutAttribute.ConstructorArguments[0].Value);
            foreach (KeyValuePair<string, TypedConstant> namedArgument in layoutAttribute.NamedArguments) {
                if (namedArgument.Key == "Pack") {
                    pack = Convert.ToInt32(namedArgument.Value.Value);
                }
            }
        }

        if (byValue && layoutKind != LayoutKind.Sequential) {
            return CPPPInvokeTypeLoweringResult.Failure("only sequential structs can cross by value", "pass it by ref or pointer");
        }

        List<CPPPInvokeMirrorField> mirrorFields = new List<CPPPInvokeMirrorField>();
        foreach (IFieldSymbol field in fields) {
            CPPPInvokeTypeLoweringResult fieldResult = LowerValue(field.Type, false, byValue);
            if (!fieldResult.Succeeded) {
                return fieldResult;
            }
            mirrorFields.Add(new CPPPInvokeMirrorField(field.Name, fieldResult.Type.MirrorTypeText));
        }

        string mirrorName = CreateMirrorName(type);
        if (byValue) {
            RegisterMirror(type, mirrorName, pack, mirrorFields);
        }
        return CPPPInvokeTypeLoweringResult.Success(new CPPPInvokeLoweredType(CPPPInvokeValueKind.Struct, mirrorName, type));
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
    /// Registers a newly lowered by-value struct as a mirror, skipping registration when a mirror for the same
    /// managed struct type was already generated.
    /// </summary>
    /// <param name="type">Managed struct type the mirror was generated from.</param>
    /// <param name="mirrorName">Generated native mirror struct name.</param>
    /// <param name="pack">Explicit struct packing in bytes, or <c>0</c> for the platform default.</param>
    /// <param name="fields">Mirror fields in layout order.</param>
    void RegisterMirror(INamedTypeSymbol type, string mirrorName, int pack, IReadOnlyList<CPPPInvokeMirrorField> fields) {
        CPPPInvokeMirrorStruct mirror = new CPPPInvokeMirrorStruct(mirrorName, type, pack, fields);
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
