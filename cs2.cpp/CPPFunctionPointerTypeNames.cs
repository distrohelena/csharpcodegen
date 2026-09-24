namespace cs2.cpp;

/// <summary>
/// Names the C++ runtime wrapper templates that C# <c>delegate*</c> types lower to and recognizes them wherever the
/// backend classifies a type name as a function pointer.
/// </summary>
public static class CPPFunctionPointerTypeNames {
    /// <summary>
    /// Wrapper name for managed <c>delegate*&lt;...&gt;</c> pointers, which use the platform default calling convention.
    /// </summary>
    public const string Managed = "FunctionPointer";

    /// <summary>
    /// Wrapper name for <c>delegate* unmanaged[Stdcall]&lt;...&gt;</c> pointers, whose target type carries <c>HE_CPP_STDCALL</c>.
    /// </summary>
    public const string Stdcall = "StdcallFunctionPointer";

    /// <summary>
    /// Wrapper name for <c>delegate* unmanaged[Cdecl]&lt;...&gt;</c> pointers, whose target type carries <c>HE_CPP_CDECL</c>.
    /// </summary>
    public const string Cdecl = "CdeclFunctionPointer";

    /// <summary>
    /// Determines whether a type name is exactly one of the managed, stdcall, or cdecl function-pointer wrapper names.
    /// </summary>
    /// <param name="name">Unqualified type name to classify.</param>
    /// <returns>True when the name denotes a function-pointer wrapper.</returns>
    public static bool IsFunctionPointerTypeName(string name) {
        return string.Equals(name, Managed, StringComparison.Ordinal) ||
            string.Equals(name, Stdcall, StringComparison.Ordinal) ||
            string.Equals(name, Cdecl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether rendered C++ type text is a function-pointer wrapper: a bare wrapper name, a wrapper template
    /// instantiation, or a namespace-qualified wrapper template instantiation.
    /// </summary>
    /// <param name="cppTypeText">Rendered C++ type text to classify.</param>
    /// <returns>True when the text names a managed, stdcall, or cdecl function-pointer wrapper.</returns>
    public static bool IsFunctionPointerCppTypeText(string cppTypeText) {
        foreach (string name in new[] { Managed, Stdcall, Cdecl }) {
            if (string.Equals(cppTypeText, name, StringComparison.Ordinal) ||
                cppTypeText.StartsWith(name + "<", StringComparison.Ordinal) ||
                cppTypeText.Contains("::" + name + "<", StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Recognizes a wrapper name in a normalized referenced-class name, where generic instantiations are flattened with
    /// underscores (for example <c>StdcallFunctionPointer_int32_t_...</c>).
    /// </summary>
    /// <param name="normalizedTypeName">Normalized referenced-class name to classify.</param>
    /// <param name="wrapperName">Matched wrapper name when the lookup succeeds; otherwise, an empty string.</param>
    /// <returns>True when the normalized name is a managed, stdcall, or cdecl function-pointer wrapper.</returns>
    public static bool TryGetWrapperNameFromNormalizedTypeName(string normalizedTypeName, out string wrapperName) {
        foreach (string name in new[] { Managed, Stdcall, Cdecl }) {
            if (string.Equals(normalizedTypeName, name, StringComparison.Ordinal) ||
                normalizedTypeName.StartsWith(name + "_", StringComparison.Ordinal)) {
                wrapperName = name;
                return true;
            }
        }
        wrapperName = string.Empty;
        return false;
    }

    /// <summary>
    /// Determines whether a type name is one of the unmanaged (stdcall or cdecl) function-pointer wrapper names, which
    /// live in <c>runtime/unmanaged_function_pointer.hpp</c> rather than <c>runtime/function_pointer.hpp</c>.
    /// </summary>
    /// <param name="name">Unqualified type name to classify.</param>
    /// <returns>True when the name denotes an unmanaged function-pointer wrapper.</returns>
    public static bool IsUnmanagedFunctionPointerTypeName(string name) {
        return string.Equals(name, Stdcall, StringComparison.Ordinal) ||
            string.Equals(name, Cdecl, StringComparison.Ordinal);
    }
}
