using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies explicit ownership contracts for framework operations whose implementations are not available to source analysis.
/// </summary>
public sealed class CPPIntrinsicOwnershipCatalogTests {
    /// <summary>
    /// Ensures known shared and allocating framework methods expose deterministic return ownership.
    /// </summary>
    /// <param name="expressionText">Framework invocation to classify.</param>
    /// <param name="expectedOwnership">Hand-verified ownership expected for its returned native value.</param>
    [Theory]
    [InlineData("System.Array.Empty<int>()", CPPOwnershipKind.Borrowed)]
    [InlineData("System.Linq.Enumerable.Empty<int>()", CPPOwnershipKind.Borrowed)]
    [InlineData("new int[4].ToArray()", CPPOwnershipKind.Owned)]
    [InlineData("new int[4].Clone()", CPPOwnershipKind.Owned)]
    [InlineData("new System.Collections.Generic.List<int>().ToArray()", CPPOwnershipKind.Owned)]
    [InlineData("System.Text.Encoding.UTF8.GetBytes(\"value\")", CPPOwnershipKind.Owned)]
    [InlineData("new System.IO.MemoryStream().ToArray()", CPPOwnershipKind.Owned)]
    [InlineData("\"value\".Split(',')", CPPOwnershipKind.Owned)]
    [InlineData("new System.Collections.Generic.List<int>().AsReadOnly()", CPPOwnershipKind.Owned)]
    [InlineData("System.IO.File.OpenRead(\"asset.bin\")", CPPOwnershipKind.Owned)]
    public void TryGetReturnOwnership_ClassifiesKnownFrameworkCalls(string expressionText, CPPOwnershipKind expectedOwnership) {
        IMethodSymbol method = OwnershipRoslynTestHelper.ResolveInvocation(expressionText);
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        bool resolved = catalog.TryGetReturnOwnership(method, out CPPOwnershipKind ownership);

        Assert.True(resolved);
        Assert.Equal(expectedOwnership, ownership);
    }

    /// <summary>
    /// Ensures unknown external factories remain unknown instead of being guessed as owned or borrowed.
    /// </summary>
    [Fact]
    public void TryGetReturnOwnership_WithUnknownFactory_ReturnsUnknown() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            public sealed class Factory {
                public static object Create() {
                    return null;
                }
            }

            public sealed class Consumer {
                public object Run() {
                    return Factory.Create();
                }
            }
            """);
        SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
        IMethodSymbol method = (IMethodSymbol)(semanticModel.GetSymbolInfo(
            syntaxTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single()).Symbol
            ?? throw new InvalidOperationException("Unknown factory invocation did not resolve."));
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        bool resolved = catalog.TryGetReturnOwnership(method, out CPPOwnershipKind ownership);

        Assert.False(resolved);
        Assert.Equal(CPPOwnershipKind.Unknown, ownership);
    }

    /// <summary>
    /// Ensures semantic parameter attributes distinguish no-escape borrowing from ownership transfer.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_UsesSemanticOwnershipAttributes() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeNoEscapeAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            public sealed class Consumer {
                public void Use([NativeNoEscape] object borrowed, [NativeTakesOwnership] object owned) {
                }
            }
            """);
        SemanticModel semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        IMethodSymbol method = (IMethodSymbol)(semanticModel.GetDeclaredSymbol(
            compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single())
            ?? throw new InvalidOperationException("Ownership fixture method did not resolve."));
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(method.Parameters[0], out CPPParameterOwnershipKind borrowed));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, borrowed);
        Assert.True(catalog.TryGetParameterOwnership(method.Parameters[1], out CPPParameterOwnershipKind owned));
        Assert.Equal(CPPParameterOwnershipKind.TakesOwnership, owned);
    }

    /// <summary>
    /// Ensures framework string helpers that only inspect collection or separator storage preserve caller ownership.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesReviewedStringHelperInputsAsNoEscape() {
        IMethodSymbol joinMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "string.Join(\", \", new System.Collections.Generic.List<string>())");
        IMethodSymbol splitMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "\"value\".Split(new[] { ' ' }, System.StringSplitOptions.None)");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(joinMethod.Parameters[1], out CPPParameterOwnershipKind joinValues));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, joinValues);
        Assert.True(catalog.TryGetParameterOwnership(splitMethod.Parameters[0], out CPPParameterOwnershipKind splitSeparators));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, splitSeparators);
    }
}
