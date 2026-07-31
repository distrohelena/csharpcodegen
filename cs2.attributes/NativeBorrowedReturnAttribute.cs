namespace cs2.attributes;

/// <summary>
/// Declares that generated native callers borrow each non-null value returned by the annotated method or property.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NativeBorrowedReturnAttribute : Attribute {
    /// <summary>
    /// Initializes the compile-time borrowed-return contract.
    /// </summary>
    public NativeBorrowedReturnAttribute() {
    }
}
