namespace cs2.cpp;

/// <summary>
/// Represents one local's native ownership and lifecycle at a precise control-flow point.
/// </summary>
public sealed class CPPLocalOwnershipState {
    /// <summary>
    /// Initializes one immutable local ownership state.
    /// </summary>
    /// <param name="ownership">Current responsibility for the native value.</param>
    /// <param name="lifecycle">Current lifetime phase for the value.</param>
    /// <param name="isInitialized">Whether the local currently carries a non-null classified value.</param>
    public CPPLocalOwnershipState(
        CPPOwnershipKind ownership,
        CPPOwnershipLifecycle lifecycle,
        bool isInitialized) {
        Ownership = ownership;
        Lifecycle = lifecycle;
        IsInitialized = isInitialized;
    }

    /// <summary>
    /// Gets current responsibility for the native value.
    /// </summary>
    public CPPOwnershipKind Ownership { get; }

    /// <summary>
    /// Gets the current lifetime phase for the value.
    /// </summary>
    public CPPOwnershipLifecycle Lifecycle { get; }

    /// <summary>
    /// Gets whether the local currently carries a non-null classified value.
    /// </summary>
    public bool IsInitialized { get; }

    /// <summary>
    /// Gets whether incompatible predecessor states made ownership unknowable.
    /// </summary>
    public bool IsAmbiguous => IsInitialized && Ownership == CPPOwnershipKind.Unknown;

    /// <summary>
    /// Determines whether another state has identical semantic values.
    /// </summary>
    /// <param name="other">State to compare.</param>
    /// <returns><c>true</c> when ownership, lifecycle, and initialization match.</returns>
    public bool SemanticallyEquals(CPPLocalOwnershipState other) {
        return other != null &&
            Ownership == other.Ownership &&
            Lifecycle == other.Lifecycle &&
            IsInitialized == other.IsInitialized;
    }

    /// <summary>
    /// Creates an uninitialized state used before a local receives a classified value.
    /// </summary>
    /// <returns>A state carrying no native value.</returns>
    public static CPPLocalOwnershipState CreateUninitialized() {
        return new CPPLocalOwnershipState(
            CPPOwnershipKind.Unknown,
            CPPOwnershipLifecycle.Live,
            false);
    }
}
