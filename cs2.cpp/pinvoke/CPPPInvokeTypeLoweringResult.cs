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
    /// <param name="isUnsupportedSetting">Whether the failure is caused by an unsupported attribute setting rather
    /// than by the type itself.</param>
    CPPPInvokeTypeLoweringResult(bool succeeded, CPPPInvokeLoweredType type, string failureReason, string recommendation, bool isUnsupportedSetting) {
        Succeeded = succeeded;
        Type = type;
        FailureReason = failureReason;
        Recommendation = recommendation;
        IsUnsupportedSetting = isUnsupportedSetting;
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
    /// Gets a value indicating whether the failure is caused by an attribute setting the backend does not support
    /// (for example <c>StructLayout.Size</c>) rather than by the type's shape. The analyzer reports such failures as
    /// <see cref="CPPPInvokeDiagnosticCodes.UnsupportedImportSetting"/> instead of the signature's type-error code.
    /// Always <c>false</c> for a successful result.
    /// </summary>
    public bool IsUnsupportedSetting { get; }

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
        return new CPPPInvokeTypeLoweringResult(true, type, null, null, false);
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
        return new CPPPInvokeTypeLoweringResult(false, null, reason, recommendation, false);
    }

    /// <summary>
    /// Creates a failed lowering result caused by an attribute setting the backend does not support, reported with
    /// <see cref="CPPPInvokeDiagnosticCodes.UnsupportedImportSetting"/>.
    /// </summary>
    /// <param name="reason">Human-readable reason the setting cannot be honored.</param>
    /// <param name="recommendation">Suggested fix for the failure.</param>
    /// <returns>A result reporting an unsupported-setting failure with the given reason and recommendation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> or <paramref name="recommendation"/> is null.</exception>
    public static CPPPInvokeTypeLoweringResult UnsupportedSetting(string reason, string recommendation) {
        if (reason == null) {
            throw new ArgumentNullException(nameof(reason));
        }
        if (recommendation == null) {
            throw new ArgumentNullException(nameof(recommendation));
        }
        return new CPPPInvokeTypeLoweringResult(false, null, reason, recommendation, true);
    }
}
