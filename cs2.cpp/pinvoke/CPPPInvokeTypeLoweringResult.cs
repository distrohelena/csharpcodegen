namespace cs2.cpp;

/// <summary>
/// Describes the outcome of lowering one managed type (a parameter or return type) to its native P/Invoke
/// representation: either the resulting lowered type, or a human-readable reason the type cannot cross the boundary
/// together with a suggested fix.
/// </summary>
public sealed class CPPPInvokeTypeLoweringResult {
    /// <summary>
    /// Creates a lowering result. Use <see cref="Success(CPPPInvokeLoweredType)"/> or
    /// <see cref="Failure(string, string)"/> instead of calling this constructor directly.
    /// </summary>
    /// <param name="succeeded">Whether lowering succeeded.</param>
    /// <param name="type">Lowered type when lowering succeeded, otherwise <c>null</c>.</param>
    /// <param name="failureReason">Human-readable reason lowering failed, otherwise <c>null</c>.</param>
    /// <param name="recommendation">Suggested fix for the failure, otherwise <c>null</c>.</param>
    CPPPInvokeTypeLoweringResult(bool succeeded, CPPPInvokeLoweredType type, string failureReason, string recommendation) {
        Succeeded = succeeded;
        Type = type;
        FailureReason = failureReason;
        Recommendation = recommendation;
    }

    /// <summary>
    /// Gets a value indicating whether the type was successfully lowered to a native representation.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the lowered type when <see cref="Succeeded"/> is <c>true</c>, otherwise <c>null</c>.
    /// </summary>
    public CPPPInvokeLoweredType Type { get; }

    /// <summary>
    /// Gets a human-readable explanation of why the type cannot cross a direct P/Invoke boundary, or <c>null</c> when
    /// <see cref="Succeeded"/> is <c>true</c>.
    /// </summary>
    public string FailureReason { get; }

    /// <summary>
    /// Gets a suggested fix for the failure (for example, an alternative type or parameter shape to use), or
    /// <c>null</c> when <see cref="Succeeded"/> is <c>true</c>.
    /// </summary>
    public string Recommendation { get; }

    /// <summary>
    /// Creates a successful lowering result.
    /// </summary>
    /// <param name="type">Lowered type.</param>
    /// <returns>A result reporting success with the given lowered type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public static CPPPInvokeTypeLoweringResult Success(CPPPInvokeLoweredType type) {
        if (type == null) {
            throw new ArgumentNullException(nameof(type));
        }
        return new CPPPInvokeTypeLoweringResult(true, type, null, null);
    }

    /// <summary>
    /// Creates a failed lowering result.
    /// </summary>
    /// <param name="reason">Human-readable reason the type cannot cross the boundary.</param>
    /// <param name="recommendation">Suggested fix for the failure.</param>
    /// <returns>A result reporting failure with the given reason and recommendation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> or <paramref name="recommendation"/> is null.</exception>
    public static CPPPInvokeTypeLoweringResult Failure(string reason, string recommendation) {
        if (reason == null) {
            throw new ArgumentNullException(nameof(reason));
        }
        if (recommendation == null) {
            throw new ArgumentNullException(nameof(recommendation));
        }
        return new CPPPInvokeTypeLoweringResult(false, null, reason, recommendation);
    }
}
