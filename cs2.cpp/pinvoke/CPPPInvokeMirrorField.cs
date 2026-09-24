namespace cs2.cpp;

/// <summary>
/// Describes one field of a generated native mirror struct: its name and the C++ type text used to declare it, in the
/// order fields must appear for the mirror's layout to match the managed struct's marshaled layout.
/// </summary>
public sealed class CPPPInvokeMirrorField {
    /// <summary>
    /// Creates a mirror field description.
    /// </summary>
    /// <param name="name">Field name as declared on the managed struct.</param>
    /// <param name="mirrorTypeText">C++ type text used to declare the field in the generated mirror struct.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="mirrorTypeText"/> is null.
    /// </exception>
    public CPPPInvokeMirrorField(string name, string mirrorTypeText) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MirrorTypeText = mirrorTypeText ?? throw new ArgumentNullException(nameof(mirrorTypeText));
    }

    /// <summary>
    /// Gets the field name as declared on the managed struct.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the C++ type text used to declare this field in the generated mirror struct.
    /// </summary>
    public string MirrorTypeText { get; }
}
