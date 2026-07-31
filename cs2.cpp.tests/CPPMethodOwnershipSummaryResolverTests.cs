using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies fixed-point ownership summaries for source-visible and contract-annotated methods.
/// </summary>
public sealed class CPPMethodOwnershipSummaryResolverTests {
    /// <summary>
    /// Ensures anonymous functions and explicit delegate wrappers are classified as fresh owned native delegate storage.
    /// </summary>
    [Fact]
    public void Resolve_WithDelegateFactories_ClassifiesReturnsAsOwned() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            public delegate int Mapper(int value);

            public static class DelegateFactory {
                public static Mapper CreateLambda() {
                    return value => value + 1;
                }

                public static Mapper CreateWrapper() {
                    return new Mapper(value => value + 1);
                }
            }
            """);

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPOwnershipKind.Owned, ResolveSummary(compilation, resolution, "CreateLambda").ReturnOwnership);
        Assert.Equal(CPPOwnershipKind.Owned, ResolveSummary(compilation, resolution, "CreateWrapper").ReturnOwnership);
        Assert.DoesNotContain(resolution.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures native runtime type tokens returned by <c>typeof</c> are classified as shared borrowed storage.
    /// </summary>
    [Fact]
    public void Resolve_WithTypeOfReturn_ClassifiesReturnAsBorrowed() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System;

            public static class TypeResolver {
                public static Type Resolve() {
                    return typeof(TypeResolver);
                }
            }
            """);

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPOwnershipKind.Borrowed, ResolveSummary(compilation, resolution, "Resolve").ReturnOwnership);
        Assert.DoesNotContain(resolution.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures fresh locals, nested factories, null branches, borrowed parameters, and mixed returns receive distinct contracts.
    /// </summary>
    [Fact]
    public void Resolve_WithFactoryGraph_ClassifiesReturnsAndRejectsMixedOwnership() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System.Collections.Generic;

            public static class FactoryGraph {
                public static List<int> Fresh() {
                    List<int> values = new List<int>();
                    return values;
                }

                public static List<int> Nested() {
                    return Fresh();
                }

                public static List<int> MaybeFresh(bool enabled) {
                    return enabled ? Nested() : null;
                }

                public static List<int> Borrowed(List<int> cached) {
                    return cached;
                }

                public static List<int> Mixed(bool enabled, List<int> cached) {
                    return enabled ? new List<int>() : cached;
                }
            }
            """);

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPOwnershipKind.Owned, ResolveSummary(compilation, resolution, "Fresh").ReturnOwnership);
        Assert.Equal(CPPOwnershipKind.Owned, ResolveSummary(compilation, resolution, "Nested").ReturnOwnership);
        Assert.Equal(CPPOwnershipKind.Owned, ResolveSummary(compilation, resolution, "MaybeFresh").ReturnOwnership);
        Assert.Equal(CPPOwnershipKind.Borrowed, ResolveSummary(compilation, resolution, "Borrowed").ReturnOwnership);
        CPPConversionDiagnostic diagnostic = Assert.Single(resolution.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN005");
        Assert.Equal("Mixed", diagnostic.SourceMemberName);
    }

    /// <summary>
    /// Ensures an owned base return establishes ownership throughout a mutually recursive source call graph.
    /// </summary>
    [Fact]
    public void Resolve_WithMutualRecursion_PropagatesOwnedReturn() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System.Collections.Generic;

            public static class RecursiveFactory {
                public static List<int> First(int depth) {
                    if (depth == 0) {
                        return new List<int>();
                    }

                    return Second(depth - 1);
                }

                public static List<int> Second(int depth) {
                    return First(depth);
                }
            }
            """);

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPOwnershipKind.Owned, ResolveSummary(compilation, resolution, "First").ReturnOwnership);
        Assert.Equal(CPPOwnershipKind.Owned, ResolveSummary(compilation, resolution, "Second").ReturnOwnership);
        Assert.DoesNotContain(resolution.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN001");
    }

    /// <summary>
    /// Ensures boundary annotations classify unavailable implementations while contradictory source annotations remain hard errors.
    /// </summary>
    [Fact]
    public void Resolve_WithReturnContracts_UsesBoundariesAndRejectsContradictions() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
            public sealed class NativeOwnedReturnAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
            public sealed class NativeBorrowedReturnAttribute : Attribute {
            }

            public abstract class ExternalFactory {
                [NativeOwnedReturn]
                public abstract List<int> Create();

                [NativeBorrowedReturn]
                public abstract List<int> Shared();

                public abstract List<int> Unknown();
            }

            public static class Consumer {
                public static List<int> Create(ExternalFactory factory) {
                    return factory.Create();
                }

                public static List<int> Shared(ExternalFactory factory) {
                    return factory.Shared();
                }

                public static List<int> Unknown(ExternalFactory factory) {
                    return factory.Unknown();
                }

                [NativeBorrowedReturn]
                public static List<int> Contradiction() {
                    return new List<int>();
                }
            }
            """);

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPOwnershipKind.Owned, ResolveSummary(compilation, resolution, "Consumer", "Create").ReturnOwnership);
        Assert.Equal(CPPOwnershipKind.Borrowed, ResolveSummary(compilation, resolution, "Consumer", "Shared").ReturnOwnership);
        Assert.Equal(CPPOwnershipKind.Unknown, ResolveSummary(compilation, resolution, "Consumer", "Unknown").ReturnOwnership);
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN001" && diagnostic.SourceMemberName == "Unknown");
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" && diagnostic.SourceMemberName == "Contradiction");
    }

    /// <summary>
    /// Ensures source-visible parameter use is summarized as no-escape, transfer, forwarding transfer, or contradictory escape.
    /// </summary>
    [Fact]
    public void Resolve_WithParameterFlows_InfersAndValidatesContracts() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeNoEscapeAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public static class ParameterFlows {
                static List<int> Shared;

                public static int ReadOnly(List<int> values) {
                    return values.Count;
                }

                public static void Destroy(List<int> values) {
                    NativeOwnership.Delete(values);
                }

                public static void Forward(List<int> values) {
                    Destroy(values);
                }

                public static void Invalid([NativeNoEscape] List<int> values) {
                    Shared = values;
                }
            }
            """);

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPParameterOwnershipKind.NoEscape, ResolveSummary(compilation, resolution, "ReadOnly").GetParameterOwnership(0));
        Assert.Equal(CPPParameterOwnershipKind.TakesOwnership, ResolveSummary(compilation, resolution, "Destroy").GetParameterOwnership(0));
        Assert.Equal(CPPParameterOwnershipKind.TakesOwnership, ResolveSummary(compilation, resolution, "Forward").GetParameterOwnership(0));
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" && diagnostic.SourceMemberName == "Invalid");
    }

    /// <summary>
    /// Ensures a parameter used as an indexer receiver inside an argument expression is not mistaken for the index argument itself.
    /// </summary>
    [Fact]
    public void Resolve_WithParameterReceiverNestedInsideIndexerArgument_InfersNoEscape() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System.Collections.Generic;

            public static class Reader {
                public static int Read(IReadOnlyList<int> values) {
                    return values[values.Count - 1];
                }
            }
            """);

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPParameterOwnershipKind.NoEscape, ResolveSummary(compilation, resolution, "Read").GetParameterOwnership(0));
    }

    /// <summary>
    /// Ensures assigning a contracted parameter into a native-owned member is recognized as an ownership transfer.
    /// </summary>
    [Fact]
    public void Resolve_WithTakesOwnershipParameterAssignedToOwnedMember_InfersTransfer() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            public sealed class Owner {
                [NativeOwnedMember]
                object Value;

                public void Take([NativeTakesOwnership] object value) {
                    Value = value;
                }
            }
            """);

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPParameterOwnershipKind.TakesOwnership, ResolveSummary(compilation, resolution, "Take").GetParameterOwnership(0));
        Assert.DoesNotContain(resolution.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" && diagnostic.SourceMemberName == "Take");
    }

    /// <summary>
    /// Ensures array-backed list-family getters are owned because the C++ lowerer materializes a new native list at the boundary.
    /// </summary>
    [Fact]
    public void Resolve_WithArrayBackedReadOnlyListGetter_ClassifiesNativeMaterializationAsOwned() {
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System.Collections.Generic;

            public sealed class Source {
                readonly int[] ItemsValue;

                public Source(int[] items) {
                    ItemsValue = items;
                }

                public IReadOnlyList<int> Items {
                    get {
                        return ItemsValue;
                    }
                }
            }
            """);
        IPropertySymbol property = compilation.GetTypeByMetadataName("Source")
            ?.GetMembers("Items")
            .OfType<IPropertySymbol>()
            .Single()
            ?? throw new InvalidOperationException("Array-backed property did not resolve.");

        CPPMethodOwnershipSummaryResolution resolution = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);

        Assert.Equal(CPPOwnershipKind.Owned, resolution.GetSummary(property.GetMethod).ReturnOwnership);
    }

    /// <summary>
    /// Resolves one method summary by containing type and method name.
    /// </summary>
    /// <param name="compilation">Compilation containing the source method.</param>
    /// <param name="resolution">Completed ownership summary resolution.</param>
    /// <param name="methodName">Method name to select.</param>
    /// <returns>The ownership summary associated with the selected source method.</returns>
    static CPPMethodOwnershipSummary ResolveSummary(
        CSharpCompilation compilation,
        CPPMethodOwnershipSummaryResolution resolution,
        string methodName) {
        return ResolveSummary(compilation, resolution, string.Empty, methodName);
    }

    /// <summary>
    /// Resolves one method summary by optional containing type and method name.
    /// </summary>
    /// <param name="compilation">Compilation containing the source method.</param>
    /// <param name="resolution">Completed ownership summary resolution.</param>
    /// <param name="typeName">Containing type name, or an empty value when the method name is unique.</param>
    /// <param name="methodName">Method name to select.</param>
    /// <returns>The ownership summary associated with the selected source method.</returns>
    static CPPMethodOwnershipSummary ResolveSummary(
        CSharpCompilation compilation,
        CPPMethodOwnershipSummaryResolution resolution,
        string typeName,
        string methodName) {
        IMethodSymbol method = ResolveMethod(compilation, typeName, methodName);
        return resolution.GetSummary(method);
    }

    /// <summary>
    /// Resolves one source method by optional containing type and method name.
    /// </summary>
    /// <param name="compilation">Compilation containing the source method.</param>
    /// <param name="typeName">Containing type name, or an empty value when the method name is unique.</param>
    /// <param name="methodName">Method name to select.</param>
    /// <returns>The resolved Roslyn method symbol.</returns>
    static IMethodSymbol ResolveMethod(CSharpCompilation compilation, string typeName, string methodName) {
        SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
        MethodDeclarationSyntax declaration = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate =>
                string.Equals(candidate.Identifier.Text, methodName, StringComparison.Ordinal) &&
                (string.IsNullOrEmpty(typeName) ||
                 string.Equals(candidate.Ancestors().OfType<TypeDeclarationSyntax>().First().Identifier.Text, typeName, StringComparison.Ordinal)));
        return semanticModel.GetDeclaredSymbol(declaration)
            ?? throw new InvalidOperationException($"Method '{methodName}' did not resolve.");
    }
}
