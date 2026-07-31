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
    /// Indicates that the callee assumes cleanup responsibility for the argument.
    /// </summary>
    TakesOwnership
}
