using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp;

/// <summary>
/// Records one source operation that changes responsibility for a generated native local value.
/// </summary>
public sealed class CPPOwnershipTransition {
    /// <summary>
    /// Initializes one semantic ownership state transition.
    /// </summary>
    /// <param name="syntax">Source syntax where the transition occurs.</param>
    /// <param name="localDeclaration">Declaration of the affected local.</param>
    /// <param name="kind">Ownership transition kind.</param>
    /// <param name="resultingOwnership">Ownership kind after the transition.</param>
    /// <param name="resultingLifecycle">Lifecycle state after the transition.</param>
    public CPPOwnershipTransition(
        SyntaxNode syntax,
        VariableDeclaratorSyntax localDeclaration,
        CPPOwnershipTransitionKind kind,
        CPPOwnershipKind resultingOwnership,
        CPPOwnershipLifecycle resultingLifecycle) {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        LocalDeclaration = localDeclaration ?? throw new ArgumentNullException(nameof(localDeclaration));
        Kind = kind;
        ResultingOwnership = resultingOwnership;
        ResultingLifecycle = resultingLifecycle;
    }

    /// <summary>
    /// Gets the source syntax where the transition occurs.
    /// </summary>
    public SyntaxNode Syntax { get; }

    /// <summary>
    /// Gets the declaration of the affected local.
    /// </summary>
    public VariableDeclaratorSyntax LocalDeclaration { get; }

    /// <summary>
    /// Gets the source local identifier.
    /// </summary>
    public string LocalName => LocalDeclaration.Identifier.Text;

    /// <summary>
    /// Gets the semantic transition kind.
    /// </summary>
    public CPPOwnershipTransitionKind Kind { get; }

    /// <summary>
    /// Gets the ownership kind after the transition.
    /// </summary>
    public CPPOwnershipKind ResultingOwnership { get; }

    /// <summary>
    /// Gets the lifecycle state after the transition.
    /// </summary>
    public CPPOwnershipLifecycle ResultingLifecycle { get; }
}
