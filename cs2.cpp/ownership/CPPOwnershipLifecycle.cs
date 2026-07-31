namespace cs2.cpp;

/// <summary>
/// Describes the lifecycle of a value after its ownership kind has been established.
/// </summary>
public enum CPPOwnershipLifecycle {
    /// <summary>
    /// Indicates that the value remains valid and available to its current owner or borrower.
    /// </summary>
    Live,

    /// <summary>
    /// Indicates that explicit native cleanup has destroyed the value.
    /// </summary>
    Released,

    /// <summary>
    /// Indicates that cleanup responsibility has moved to another verified owner.
    /// </summary>
    Transferred,

    /// <summary>
    /// Indicates that the declaring lexical scope retains responsibility for automatic cleanup.
    /// </summary>
    ScopeCleanup
}
