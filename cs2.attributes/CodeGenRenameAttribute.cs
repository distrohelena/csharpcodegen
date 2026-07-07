namespace cs2.attributes;

/// <summary>
/// Requests one explicit generated type name for the annotated declaration.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Struct |
    AttributeTargets.Interface |
    AttributeTargets.Enum |
    AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = false)]
public sealed class CodeGenRenameAttribute : Attribute {
    /// <summary>
    /// Gets the emitted type name requested by the source declaration.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes one source-level generated type rename contract.
    /// </summary>
    /// <param name="name">Requested emitted type name.</param>
    public CodeGenRenameAttribute(string name) {
        Name = name;
    }
}
