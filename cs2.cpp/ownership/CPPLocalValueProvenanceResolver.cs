using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp;

/// <summary>
/// Resolves an unchanged local alias back to the source expression that established its native object identity.
/// </summary>
public static class CPPLocalValueProvenanceResolver {
    /// <summary>
    /// Resolves a local declaration initializer or type-pattern input when no later assignment can replace the alias.
    /// </summary>
    /// <param name="method">Method containing the local alias.</param>
    /// <param name="local">Local whose stable source expression should be resolved.</param>
    /// <param name="semanticModel">Semantic model for the method body.</param>
    /// <param name="sourceExpression">Stable expression that supplied the local value when provenance is provable.</param>
    /// <returns><c>true</c> when the local has one unchanged source expression.</returns>
    public static bool TryResolveStableSourceExpression(
        IMethodSymbol method,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out ExpressionSyntax sourceExpression) {
        if (method == null) {
            throw new ArgumentNullException(nameof(method));
        } else if (local == null) {
            throw new ArgumentNullException(nameof(local));
        } else if (semanticModel == null) {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        sourceExpression = ResolveSourceExpression(local);
        if (sourceExpression == null) {
            return false;
        }

        SyntaxNode methodDeclaration = method.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .FirstOrDefault();
        if (methodDeclaration == null) {
            return false;
        }

        foreach (AssignmentExpressionSyntax assignment in methodDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>()) {
            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left).Symbol, local)) {
                sourceExpression = null;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the declaration expression that supplied one local's initial object identity.
    /// </summary>
    /// <param name="local">Local whose declaring syntax should be inspected.</param>
    /// <returns>The initializer or type-pattern input expression, or null when the declaration does not expose one.</returns>
    static ExpressionSyntax ResolveSourceExpression(ILocalSymbol local) {
        SyntaxNode declaration = local.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .FirstOrDefault();
        if (declaration is VariableDeclaratorSyntax variableDeclarator) {
            return variableDeclarator.Initializer?.Value;
        } else if (declaration is SingleVariableDesignationSyntax variableDesignation) {
            IsPatternExpressionSyntax isPatternExpression = variableDesignation.Ancestors()
                .OfType<IsPatternExpressionSyntax>()
                .FirstOrDefault();
            return isPatternExpression?.Expression;
        }

        return null;
    }
}
