namespace cs2.attributes;

/// <summary>
/// Declares that a generated native callee assumes cleanup responsibility for the value passed to the annotated parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class NativeTakesOwnershipAttribute : Attribute {
    /// <summary>
    /// Initializes the compile-time ownership-transfer contract.
    /// </summary>
    public NativeTakesOwnershipAttribute() {
    }
}
