namespace cs2.cpp;

/// <summary>
/// Formats C++ declarations that pair a lowered P/Invoke type text with a declared name. Most lowered types are plain
/// type names (<c>int32_t x</c>), but function-pointer mirror texts such as <c>int32_t (HE_CPP_STDCALL*)(int32_t)</c>
/// require the name to sit inside the declarator (<c>int32_t (HE_CPP_STDCALL* x)(int32_t)</c>) to be valid C++.
/// </summary>
public static class CPPPInvokeDeclaratorFormatter {
    /// <summary>
    /// Marks the pointer declarator slot of a function-pointer mirror text. Function-pointer returns are rejected by
    /// lowering, so the first occurrence always belongs to the outermost function pointer.
    /// </summary>
    const string FunctionPointerDeclaratorMarker = "*)(";

    /// <summary>
    /// Builds the declaration text for a variable, parameter, or field of the given lowered type.
    /// </summary>
    /// <param name="typeText">Lowered C++ type text, as found in <see cref="CPPPInvokeLoweredType.MirrorTypeText"/>,
    /// <see cref="CPPPInvokeParameter.MirrorParameterText"/>, or <see cref="CPPPInvokeMirrorField.MirrorTypeText"/>.</param>
    /// <param name="name">Already-sanitized C++ identifier to declare.</param>
    /// <returns>The declaration text, with the name placed inside the declarator for function-pointer types.</returns>
    /// <exception cref="ArgumentException"><paramref name="typeText"/> or <paramref name="name"/> is null or empty.</exception>
    public static string Format(string typeText, string name) {
        if (string.IsNullOrEmpty(typeText)) {
            throw new ArgumentException("Type text must not be empty.", nameof(typeText));
        }
        if (string.IsNullOrEmpty(name)) {
            throw new ArgumentException("Declared name must not be empty.", nameof(name));
        }

        int markerIndex = typeText.IndexOf(FunctionPointerDeclaratorMarker, StringComparison.Ordinal);
        if (markerIndex < 0) {
            return typeText + " " + name;
        }
        return typeText.Substring(0, markerIndex + 1) + " " + name + typeText.Substring(markerIndex + 1);
    }
}
