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
    [InlineData("System.Security.Cryptography.SHA256.HashData(new byte[1])", CPPOwnershipKind.Owned)]
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

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeRetainsBorrowAttribute : Attribute {
            }

            public sealed class Consumer {
                public void Use(
                    [NativeNoEscape] object borrowed,
                    [NativeTakesOwnership] object owned,
                    [NativeRetainsBorrow] object retainedBorrow) {
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
        Assert.True(catalog.TryGetParameterOwnership(method.Parameters[2], out CPPParameterOwnershipKind retainedBorrow));
        Assert.Equal(CPPParameterOwnershipKind.RetainsBorrow, retainedBorrow);
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

    /// <summary>
    /// Ensures framework array copying borrows both source and destination storage for only the duration of the call.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesArrayCopyBuffersAsNoEscape() {
        IMethodSymbol copyMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "System.Array.Copy(new int[1], new int[1], 1)");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(copyMethod.Parameters[0], out CPPParameterOwnershipKind source));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, source);
        Assert.True(catalog.TryGetParameterOwnership(copyMethod.Parameters[1], out CPPParameterOwnershipKind destination));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, destination);
    }

    /// <summary>
    /// Ensures stream copying borrows the destination stream while preserving its caller-owned lifetime.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesStreamCopyDestinationAsNoEscape() {
        IMethodSymbol copyMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "new System.IO.MemoryStream().CopyTo(new System.IO.MemoryStream())");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(copyMethod.Parameters[0], out CPPParameterOwnershipKind destination));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, destination);
    }

    /// <summary>
    /// Ensures list range insertion borrows the source sequence while the destination copies its references.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesListAddRangeSourceAsNoEscape() {
        IMethodSymbol addRangeMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "new System.Collections.Generic.List<int>().AddRange(new int[1])");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(addRangeMethod.Parameters[0], out CPPParameterOwnershipKind source));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, source);
    }

    /// <summary>
    /// Ensures list insertion consumes ownership of an inserted owned element so no scope-end delete can dangle the stored entry.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesListAddElementAsTakesOwnership() {
        IMethodSymbol addMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "new System.Collections.Generic.List<object>().Add(new object())");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(addMethod.Parameters[0], out CPPParameterOwnershipKind item));
        Assert.Equal(CPPParameterOwnershipKind.TakesOwnership, item);
    }

    /// <summary>
    /// Ensures dictionary insertion consumes ownership of inserted owned keys and values so no scope-end delete can dangle the stored entry.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesDictionaryAddEntriesAsTakesOwnership() {
        IMethodSymbol addMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "new System.Collections.Generic.Dictionary<object, object>().Add(new object(), new object())");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(addMethod.Parameters[0], out CPPParameterOwnershipKind key));
        Assert.Equal(CPPParameterOwnershipKind.TakesOwnership, key);
        Assert.True(catalog.TryGetParameterOwnership(addMethod.Parameters[1], out CPPParameterOwnershipKind value));
        Assert.Equal(CPPParameterOwnershipKind.TakesOwnership, value);
    }

    /// <summary>
    /// Ensures UTF-8 decoding borrows the source byte array while preserving its caller-owned lifetime.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesEncodingGetStringBytesAsNoEscape() {
        IMethodSymbol getStringMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "System.Text.Encoding.UTF8.GetString(new byte[1])");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(getStringMethod.Parameters[0], out CPPParameterOwnershipKind bytes));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, bytes);
    }

    /// <summary>
    /// Ensures SHA-256 hashing borrows its source bytes and returns an independently owned digest array.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesSha256SourceAsNoEscape() {
        IMethodSymbol hashMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "System.Security.Cryptography.SHA256.HashData(new byte[1])");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(hashMethod.Parameters[0], out CPPParameterOwnershipKind source));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, source);
    }

    /// <summary>
    /// Ensures stream writes borrow the source byte array while preserving its caller-owned lifetime.
    /// </summary>
    [Fact]
    public void TryGetParameterOwnership_ClassifiesStreamWriteBufferAsNoEscape() {
        IMethodSymbol writeMethod = OwnershipRoslynTestHelper.ResolveInvocation(
            "new System.IO.MemoryStream().Write(new byte[1], 0, 1)");
        CPPIntrinsicOwnershipCatalog catalog = new CPPIntrinsicOwnershipCatalog();

        Assert.True(catalog.TryGetParameterOwnership(writeMethod.Parameters[0], out CPPParameterOwnershipKind buffer));
        Assert.Equal(CPPParameterOwnershipKind.NoEscape, buffer);
    }
}
