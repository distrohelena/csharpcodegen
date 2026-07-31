namespace cs2.attributes;

/// <summary>
/// Declares that generated native callers assume ownership of each non-null value returned by the annotated method or property.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NativeOwnedReturnAttribute : Attribute {
    /// <summary>
    /// Initializes the compile-time owned-return contract.
    /// </summary>
    public NativeOwnedReturnAttribute() {
    }
}
