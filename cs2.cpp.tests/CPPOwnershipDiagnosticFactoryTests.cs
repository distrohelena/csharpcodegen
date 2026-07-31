using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies source-located hard errors produced by semantic native ownership analysis.
/// </summary>
public sealed class CPPOwnershipDiagnosticFactoryTests {
    /// <summary>
    /// Ensures ownership diagnostics identify the exact one-based C# source position and containing method.
    /// </summary>
    [Fact]
    public void Create_UsesOneBasedSourceCoordinates() {
        string source = """
            using System.Collections.Generic;

            public sealed class Widget {
                public void Run() {
                    List<int> values = null;
                }
            }
            """;
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation(source, "Widget.cs");
        SyntaxNode node = OwnershipRoslynTestHelper.ResolveNode(compilation, "List<int> values");
        IMethodSymbol member = OwnershipRoslynTestHelper.ResolveContainingMethod(compilation, node);

        CPPConversionDiagnostic diagnostic = new CPPOwnershipDiagnosticFactory().Create(
            "CPPOWN001",
            node,
            member,
            "Ownership cannot be inferred for local 'values'.",
            "Declare an owned or borrowed return contract at the unresolved boundary.");

        Assert.Equal(CPPDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("CPPOWN001", diagnostic.Code);
        Assert.Equal("Widget", diagnostic.SourceTypeName);
        Assert.Equal("Run", diagnostic.SourceMemberName);
        Assert.Equal("Widget.cs", Path.GetFileName(diagnostic.FilePath));
        Assert.Equal(5, diagnostic.LineNumber);
        Assert.Equal(9, diagnostic.ColumnNumber);
        Assert.Contains("values", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("owned or borrowed", diagnostic.Recommendation, StringComparison.Ordinal);
    }
}
