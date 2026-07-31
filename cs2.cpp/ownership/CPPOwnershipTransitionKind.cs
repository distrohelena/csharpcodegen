namespace cs2.cpp;

/// <summary>
/// Identifies one semantic state transition affecting a generated native pointer lifetime.
/// </summary>
public enum CPPOwnershipTransitionKind {
    /// <summary>
    /// Indicates that a local begins owning a newly produced value.
    /// </summary>
    Acquire,

    /// <summary>
    /// Indicates that a local destroys its current value and assumes ownership of a replacement.
    /// </summary>
    Replace,

    /// <summary>
    /// Indicates that explicit native cleanup destroys the current value.
    /// </summary>
    Release,

    /// <summary>
    /// Indicates that cleanup responsibility moves to a verified owner.
    /// </summary>
    Transfer,

    /// <summary>
    /// Indicates that automatic lexical-scope cleanup remains responsible for the value.
    /// </summary>
    ScopeCleanup
}
