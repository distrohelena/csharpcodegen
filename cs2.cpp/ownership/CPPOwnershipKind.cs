namespace cs2.cpp;

/// <summary>
/// Describes whether one generated native pointer value owns its storage, borrows storage, or lacks a provable contract.
/// </summary>
public enum CPPOwnershipKind {
    /// <summary>
    /// Indicates that ownership analysis could not prove a safe lifetime contract.
    /// </summary>
    Unknown,

    /// <summary>
    /// Indicates that the current value is responsible for exactly one native cleanup unless ownership is transferred.
    /// </summary>
    Owned,

    /// <summary>
    /// Indicates that the current value may be used but must not be deleted by the current scope.
    /// </summary>
    Borrowed
}
