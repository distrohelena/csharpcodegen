using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Resolves compact Roslyn fixtures used by semantic native-ownership tests.
/// </summary>
public static class OwnershipRoslynTestHelper {
    /// <summary>
    /// Creates a synthetic compilation with a deterministic assembly and source-file identity.
    /// </summary>
    /// <param name="source">C# source to compile.</param>
    /// <param name="filePath">Source path attached to the syntax tree.</param>
    /// <param name="assemblyName">Assembly identity used by stable ownership method keys.</param>
    /// <returns>A compilation ready for ownership analysis.</returns>
    public static CSharpCompilation CreateCompilation(string source, string filePath = "Fixture.cs", string assemblyName = "Fixture") {
        return RoslynTestHelper.CreateCompilation(source, assemblyName, filePath: filePath);
    }

    /// <summary>
    /// Resolves the method invoked by one standalone expression in a complete semantic fixture.
    /// </summary>
    /// <param name="expressionText">Invocation expression to bind.</param>
    /// <returns>The resolved invocation target method.</returns>
    public static IMethodSymbol ResolveInvocation(string expressionText) {
        string source = $$"""
            using System;
            using System.Linq;

            public sealed class Fixture {
                public object Run() {
                    return {{expressionText}};
                }
            }
            """;
        CSharpCompilation compilation = CreateCompilation(source);
        SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
        InvocationExpressionSyntax invocation = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Last();
        return (IMethodSymbol)(semanticModel.GetSymbolInfo(invocation).Symbol
            ?? throw new InvalidOperationException($"Invocation '{expressionText}' did not resolve to a method."));
    }

    /// <summary>
    /// Resolves the smallest syntax node containing one source marker.
    /// </summary>
    /// <param name="compilation">Compilation containing the source marker.</param>
    /// <param name="marker">Source text that uniquely identifies the desired node.</param>
    /// <returns>The narrowest syntax node containing the marker.</returns>
    public static SyntaxNode ResolveNode(CSharpCompilation compilation, string marker) {
        if (compilation == null) {
            throw new ArgumentNullException(nameof(compilation));
        }
        if (string.IsNullOrWhiteSpace(marker)) {
            throw new ArgumentException("A syntax marker is required.", nameof(marker));
        }

        return compilation.SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodesAndSelf()
            .Where(node => node.ToString().Contains(marker, StringComparison.Ordinal))
            .OrderBy(node => node.Span.Length)
            .First();
    }

    /// <summary>
    /// Resolves the method symbol containing one syntax node from the same compilation.
    /// </summary>
    /// <param name="compilation">Compilation that owns the supplied node.</param>
    /// <param name="node">Syntax node nested inside a method declaration.</param>
    /// <returns>The containing source method symbol.</returns>
    public static IMethodSymbol ResolveContainingMethod(CSharpCompilation compilation, SyntaxNode node) {
        if (compilation == null) {
            throw new ArgumentNullException(nameof(compilation));
        }
        if (node == null) {
            throw new ArgumentNullException(nameof(node));
        }

        BaseMethodDeclarationSyntax declaration = node.AncestorsAndSelf()
            .OfType<BaseMethodDeclarationSyntax>()
            .First();
        SemanticModel semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
        return semanticModel.GetDeclaredSymbol(declaration)
            ?? throw new InvalidOperationException("The containing method declaration did not resolve to a symbol.");
    }
}
