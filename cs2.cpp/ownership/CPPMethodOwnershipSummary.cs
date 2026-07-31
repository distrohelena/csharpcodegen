namespace cs2.cpp;

/// <summary>
/// Records the resolved native ownership contract for one method return and its ownership-bearing parameters.
/// </summary>
public sealed class CPPMethodOwnershipSummary {
    /// <summary>
    /// Stores immutable parameter contracts keyed by source parameter ordinal.
    /// </summary>
    readonly IReadOnlyDictionary<int, CPPParameterOwnershipKind> ParameterOwnershipValues;

    /// <summary>
    /// Initializes one complete method ownership summary.
    /// </summary>
    /// <param name="methodKey">Stable assembly-qualified method identity.</param>
    /// <param name="requiresReturnOwnership">Whether the generated return type requires pointer lifetime classification.</param>
    /// <param name="returnOwnership">Resolved return ownership, or unknown when unresolved.</param>
    /// <param name="parameterOwnership">Parameter contracts keyed by source ordinal.</param>
    public CPPMethodOwnershipSummary(
        string methodKey,
        bool requiresReturnOwnership,
        CPPOwnershipKind returnOwnership,
        IReadOnlyDictionary<int, CPPParameterOwnershipKind> parameterOwnership) {
        if (string.IsNullOrWhiteSpace(methodKey)) {
            throw new ArgumentException("A method ownership summary requires a stable key.", nameof(methodKey));
        }

        MethodKey = methodKey;
        RequiresReturnOwnership = requiresReturnOwnership;
        ReturnOwnership = returnOwnership;
        ParameterOwnershipValues = parameterOwnership != null
            ? new Dictionary<int, CPPParameterOwnershipKind>(parameterOwnership)
            : throw new ArgumentNullException(nameof(parameterOwnership));
    }

    /// <summary>
    /// Gets the stable assembly-qualified method identity.
    /// </summary>
    public string MethodKey { get; }

    /// <summary>
    /// Gets whether the generated return type requires native pointer ownership classification.
    /// </summary>
    public bool RequiresReturnOwnership { get; }

    /// <summary>
    /// Gets the resolved ownership of each non-null returned native value.
    /// </summary>
    public CPPOwnershipKind ReturnOwnership { get; }

    /// <summary>
    /// Gets immutable parameter ownership contracts keyed by source ordinal.
    /// </summary>
    public IReadOnlyDictionary<int, CPPParameterOwnershipKind> ParameterOwnership => ParameterOwnershipValues;

    /// <summary>
    /// Gets one parameter ownership contract, returning unknown when the parameter has no resolved contract.
    /// </summary>
    /// <param name="ordinal">Zero-based source parameter ordinal.</param>
    /// <returns>The resolved parameter ownership behavior.</returns>
    public CPPParameterOwnershipKind GetParameterOwnership(int ordinal) {
        return ParameterOwnershipValues.TryGetValue(ordinal, out CPPParameterOwnershipKind ownership)
            ? ownership
            : CPPParameterOwnershipKind.Unknown;
    }
}
