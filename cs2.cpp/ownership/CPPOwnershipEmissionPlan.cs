using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp;

/// <summary>
/// Provides immutable local cleanup plans and source ownership transitions to the C++ emitter.
/// </summary>
public sealed class CPPOwnershipEmissionPlan {
    /// <summary>
    /// Stores local plans keyed by exact declaration syntax.
    /// </summary>
    readonly IReadOnlyDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> LocalPlanValues;

    /// <summary>
    /// Stores ownership transitions in deterministic source order.
    /// </summary>
    readonly IReadOnlyList<CPPOwnershipTransition> TransitionValues;

    /// <summary>
    /// Initializes one immutable ownership emission plan.
    /// </summary>
    /// <param name="localPlans">Local plans keyed by declaration syntax.</param>
    /// <param name="transitions">Semantic transitions in source order.</param>
    public CPPOwnershipEmissionPlan(
        IReadOnlyDictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> localPlans,
        IReadOnlyList<CPPOwnershipTransition> transitions) {
        LocalPlanValues = localPlans != null
            ? new Dictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan>(localPlans)
            : throw new ArgumentNullException(nameof(localPlans));
        TransitionValues = transitions != null
            ? transitions.OrderBy(transition => transition.Syntax.SyntaxTree?.FilePath, StringComparer.Ordinal)
                .ThenBy(transition => transition.Syntax.SpanStart)
                .ToArray()
            : throw new ArgumentNullException(nameof(transitions));
    }

    /// <summary>
    /// Gets ownership transitions in deterministic source order.
    /// </summary>
    public IReadOnlyList<CPPOwnershipTransition> Transitions => TransitionValues;

    /// <summary>
    /// Tries to resolve the ownership plan for one exact local declaration.
    /// </summary>
    /// <param name="declaration">Source local declaration.</param>
    /// <param name="plan">Resolved local plan when present.</param>
    /// <returns><c>true</c> when the declaration is ownership-relevant.</returns>
    public bool TryGetLocalPlan(VariableDeclaratorSyntax declaration, out CPPLocalOwnershipPlan plan) {
        return LocalPlanValues.TryGetValue(declaration, out plan);
    }

    /// <summary>
    /// Tries to resolve the first ownership transition attached to one exact source syntax node.
    /// </summary>
    /// <param name="syntax">Source syntax where an ownership transition may occur.</param>
    /// <param name="transition">Resolved transition when present.</param>
    /// <returns><c>true</c> when a transition is attached to the syntax.</returns>
    public bool TryGetTransition(SyntaxNode syntax, out CPPOwnershipTransition transition) {
        transition = TransitionValues.FirstOrDefault(candidate => ReferenceEquals(candidate.Syntax, syntax));
        return transition != null;
    }
}
