namespace cs2.cpp;

/// <summary>
/// Describes one field of a generated native mirror struct: its name and the C++ type text used to declare it, in the
/// order fields must appear for the mirror's layout to match the managed struct's marshaled layout. Fields of an
/// explicit-layout struct also carry their <c>FieldOffset</c>, which the layout assertions check directly because such
/// structs have no declared mirror.
/// </summary>
public sealed class CPPPInvokeMirrorField {
    /// <summary>
    /// Holds the field's <c>FieldOffset</c> in bytes when <see cref="HasExplicitOffset"/> is <c>true</c>.
    /// </summary>
    readonly int Offset;

    /// <summary>
    /// Creates a mirror field description for a sequential-layout struct.
    /// </summary>
    /// <param name="name">Field name as declared on the managed struct.</param>
    /// <param name="mirrorTypeText">C++ type text used to declare the field in the generated mirror struct.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="mirrorTypeText"/> is null.
    /// </exception>
    public CPPPInvokeMirrorField(string name, string mirrorTypeText) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MirrorTypeText = mirrorTypeText ?? throw new ArgumentNullException(nameof(mirrorTypeText));
        HasExplicitOffset = false;
    }

    /// <summary>
    /// Creates a mirror field description for an explicit-layout struct, recording the field's declared offset.
    /// </summary>
    /// <param name="name">Field name as declared on the managed struct.</param>
    /// <param name="mirrorTypeText">C++ type text of the field's native representation, used to size it.</param>
    /// <param name="explicitOffset">The field's <c>FieldOffset</c> in bytes.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="mirrorTypeText"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="explicitOffset"/> is negative.</exception>
    public CPPPInvokeMirrorField(string name, string mirrorTypeText, int explicitOffset) {
        if (explicitOffset < 0) {
            throw new ArgumentOutOfRangeException(nameof(explicitOffset), "A FieldOffset cannot be negative.");
        }
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MirrorTypeText = mirrorTypeText ?? throw new ArgumentNullException(nameof(mirrorTypeText));
        Offset = explicitOffset;
        HasExplicitOffset = true;
    }

    /// <summary>
    /// Gets the field name as declared on the managed struct. Accepted fields never need C++ renaming, so this is
    /// also the member name of both the generated struct and its mirror.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the C++ type text used to declare this field in the generated mirror struct.
    /// </summary>
    public string MirrorTypeText { get; }

    /// <summary>
    /// Gets a value indicating whether this field belongs to an explicit-layout struct and carries a <c>FieldOffset</c>.
    /// </summary>
    public bool HasExplicitOffset { get; }

    /// <summary>
    /// Gets the field's <c>FieldOffset</c> in bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The field belongs to a sequential-layout struct.</exception>
    public int ExplicitOffset {
        get {
            if (!HasExplicitOffset) {
                throw new InvalidOperationException($"Mirror field '{Name}' belongs to a sequential struct and has no FieldOffset.");
            }
            return Offset;
        }
    }
}
