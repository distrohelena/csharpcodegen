using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp;

/// <summary>
/// Collects the local plans, transitions and diagnostics produced while analyzing one syntax tree on one worker.
/// </summary>
public sealed class CPPOwnershipTreeAnalysis {
    /// <summary>
    /// Local ownership plans keyed by declarator, for this tree only.
    /// </summary>
    public Dictionary<VariableDeclaratorSyntax, CPPLocalOwnershipPlan> LocalPlans { get; } = [];

    /// <summary>
    /// Ownership transitions discovered in this tree, in analysis order.
    /// </summary>
    public List<CPPOwnershipTransition> Transitions { get; } = [];

    /// <summary>
    /// Hard ownership errors discovered in this tree, in analysis order.
    /// </summary>
    public List<CPPConversionDiagnostic> Diagnostics { get; } = [];
}
