using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp;

/// <summary>
/// Describes the native cleanup state emitted for one ownership-relevant source local.
/// </summary>
public sealed class CPPLocalOwnershipPlan {
    /// <summary>
    /// Initializes one local ownership emission plan.
    /// </summary>
    /// <param name="declaration">Source local declaration.</param>
    /// <param name="initialOwnership">Ownership established by the initializer.</param>
    /// <param name="ownershipFlagName">Stable generated C++ ownership-flag identifier.</param>
    /// <param name="requiresScopeGuard">Whether lexical scope exit must conditionally delete the local.</param>
    /// <param name="initiallyOwnsValue">Whether the generated ownership flag starts true at declaration.</param>
    public CPPLocalOwnershipPlan(
        VariableDeclaratorSyntax declaration,
        CPPOwnershipKind initialOwnership,
        string ownershipFlagName,
        bool requiresScopeGuard,
        bool initiallyOwnsValue = true) {
        Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        if (string.IsNullOrWhiteSpace(ownershipFlagName)) {
            throw new ArgumentException("A local ownership plan requires a generated flag name.", nameof(ownershipFlagName));
        }

        InitialOwnership = initialOwnership;
        OwnershipFlagName = ownershipFlagName;
        RequiresScopeGuard = requiresScopeGuard;
        InitiallyOwnsValue = initiallyOwnsValue;
    }

    /// <summary>
    /// Gets the source local declaration.
    /// </summary>
    public VariableDeclaratorSyntax Declaration { get; }

    /// <summary>
    /// Gets the source local identifier.
    /// </summary>
    public string LocalName => Declaration.Identifier.Text;

    /// <summary>
    /// Gets the ownership established by the initializer.
    /// </summary>
    public CPPOwnershipKind InitialOwnership { get; }

    /// <summary>
    /// Gets the stable generated C++ flag tracking whether the scope still owns the local.
    /// </summary>
    public string OwnershipFlagName { get; }

    /// <summary>
    /// Gets whether lexical scope exit must conditionally delete the local.
    /// </summary>
    public bool RequiresScopeGuard { get; }

    /// <summary>
    /// Gets whether the generated ownership flag starts true when the declaration executes.
    /// </summary>
    public bool InitiallyOwnsValue { get; }
}
