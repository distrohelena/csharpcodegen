namespace cs2.attributes;

/// <summary>
/// Declares that the annotated field or property owns its generated native value and must prove replacement and teardown cleanup.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NativeOwnedMemberAttribute : Attribute {
    /// <summary>
    /// Initializes the compile-time owned-member contract.
    /// </summary>
    public NativeOwnedMemberAttribute() {
    }
}
