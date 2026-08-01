namespace cs2.cpp;

/// <summary>
/// Describes how a generated native method treats ownership-bearing arguments.
/// </summary>
public enum CPPParameterOwnershipKind {
    /// <summary>
    /// Indicates that argument escape or transfer behavior cannot be proven.
    /// </summary>
    Unknown,

    /// <summary>
    /// Indicates that the callee uses the argument only for the duration of the call.
    /// </summary>
    NoEscape,

    /// <summary>
    /// Indicates that the callee retains or returns the argument without assuming cleanup responsibility.
    /// </summary>
    Escapes,

    /// <summary>
    /// Indicates that the argument is retained only by the object returned from the call.
    /// </summary>
    EscapesWithReturn,

    /// <summary>
    /// Indicates that the callee retains a non-owning reference while cleanup responsibility remains with the caller.
    /// </summary>
    RetainsBorrow,

    /// <summary>
    /// Indicates that the callee assumes cleanup responsibility for the argument.
    /// </summary>
    TakesOwnership
}
