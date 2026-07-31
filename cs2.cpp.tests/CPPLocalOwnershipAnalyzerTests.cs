using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies local native ownership lifecycle analysis before C++ emission.
/// </summary>
public sealed class CPPLocalOwnershipAnalyzerTests {
    /// <summary>
    /// Ensures owned factories receive cleanup plans while release and transfer sites disarm their local ownership.
    /// </summary>
    [Fact]
    public void Analyze_WithOwnedBorrowedReleaseAndTransfer_ProducesEmissionPlans() {
        CSharpCompilation compilation = CreateCompilation("""
            public sealed class Consumer {
                public void Run(List<int> cached, Sink sink) {
                    List<int> direct = new List<int>();
                    List<int> built = Factory.Build();
                    List<int> shared = cached;
                    NativeOwnership.Delete(direct);
                    sink.Take(built);
                    Use(shared.Count);
                }

                static void Use(int value) {
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        CPPLocalOwnershipPlan directPlan = ResolveLocalPlan(compilation, result, "direct");
        CPPLocalOwnershipPlan builtPlan = ResolveLocalPlan(compilation, result, "built");
        CPPLocalOwnershipPlan sharedPlan = ResolveLocalPlan(compilation, result, "shared");
        Assert.True(directPlan.RequiresScopeGuard);
        Assert.Equal(CPPOwnershipKind.Owned, directPlan.InitialOwnership);
        Assert.True(builtPlan.RequiresScopeGuard);
        Assert.Equal(CPPOwnershipKind.Owned, builtPlan.InitialOwnership);
        Assert.False(sharedPlan.RequiresScopeGuard);
        Assert.Equal(CPPOwnershipKind.Borrowed, sharedPlan.InitialOwnership);
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Release && transition.LocalName == "direct");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "built");
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// Ensures an owned local returned from an owned-return method transfers instead of being deleted by its caller scope.
    /// </summary>
    [Fact]
    public void Analyze_WithOwnedLocalReturn_RecordsTransfer() {
        CSharpCompilation compilation = CreateCompilation("""
            public static class Consumer {
                public static List<int> Build() {
                    List<int> values = new List<int>();
                    return values;
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "values");
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// Ensures reading a local after explicit native deletion is rejected.
    /// </summary>
    [Fact]
    public void Analyze_WithUseAfterRelease_ReportsCPPOWN004() {
        CSharpCompilation compilation = CreateCompilation("""
            public static class Consumer {
                public static void Run() {
                    List<int> values = new List<int>();
                    NativeOwnership.Delete(values);
                    values.Add(1);
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN004");
    }

    /// <summary>
    /// Ensures reading a local after a takes-ownership call is rejected.
    /// </summary>
    [Fact]
    public void Analyze_WithUseAfterTransfer_ReportsCPPOWN004() {
        CSharpCompilation compilation = CreateCompilation("""
            public static class Consumer {
                public static void Run(Sink sink) {
                    List<int> values = new List<int>();
                    sink.Take(values);
                    values.Add(1);
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN004");
    }

    /// <summary>
    /// Ensures borrowed storage cannot be destroyed by the borrowing method.
    /// </summary>
    [Fact]
    public void Analyze_WithBorrowedDelete_ReportsCPPOWN003() {
        CSharpCompilation compilation = CreateCompilation("""
            public static class Consumer {
                public static void Run(List<int> cached) {
                    List<int> shared = cached;
                    NativeOwnership.Delete(shared);
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN003");
    }

    /// <summary>
    /// Ensures an owned local cannot cross an external parameter without a no-escape or takes-ownership contract.
    /// </summary>
    [Fact]
    public void Analyze_WithUnknownExternalParameter_ReportsCPPOWN001() {
        CSharpCompilation compilation = CreateCompilation("""
            public static class Consumer {
                public static void Run(UnknownSink sink) {
                    List<int> values = new List<int>();
                    sink.Store(values);
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN001");
    }

    /// <summary>
    /// Ensures assigning owned storage into an ordinary field is rejected as an unproven escape.
    /// </summary>
    [Fact]
    public void Analyze_WithOrdinaryFieldEscape_ReportsCPPOWN002() {
        CSharpCompilation compilation = CreateCompilation("""
            public sealed class Consumer {
                List<int> Stored;

                public void Run() {
                    List<int> values = new List<int>();
                    Stored = values;
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002");
    }

    /// <summary>
    /// Ensures constructor bodies receive the same local escape validation as ordinary methods.
    /// </summary>
    [Fact]
    public void Analyze_WithConstructorLocalFieldEscape_ReportsCPPOWN002() {
        CSharpCompilation compilation = CreateCompilation("""
            public sealed class Consumer {
                List<int> Stored;

                public Consumer() {
                    List<int> values = new List<int>();
                    Stored = values;
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002" && diagnostic.SourceMemberName == ".ctor");
    }

    /// <summary>
    /// Ensures property accessors receive the same local escape validation as methods and constructors.
    /// </summary>
    [Fact]
    public void Analyze_WithAccessorLocalFieldEscape_ReportsCPPOWN002() {
        CSharpCompilation compilation = CreateCompilation("""
            public sealed class Consumer {
                List<int> Stored;

                public int Trigger {
                    set {
                        List<int> values = new List<int>();
                        Stored = values;
                    }
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002" && diagnostic.SourceMemberName == "set_Trigger");
    }

    /// <summary>
    /// Ensures local-function bodies receive native ownership analysis through Roslyn's nested control-flow graph.
    /// </summary>
    [Fact]
    public void Analyze_WithLocalFunctionFieldEscape_ReportsCPPOWN002() {
        CSharpCompilation compilation = CreateCompilation("""
            public sealed class Consumer {
                List<int> Stored;

                public void Run() {
                    void Escape() {
                        List<int> values = new List<int>();
                        Stored = values;
                    }

                    Escape();
                }
            }
            """);

        CPPOwnershipAnalysisResult result = Analyze(compilation);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002" && diagnostic.SourceMemberName == "Escape");
    }

    /// <summary>
    /// Creates a complete ownership fixture around one test-specific consumer declaration.
    /// </summary>
    /// <param name="consumerSource">Consumer type under analysis.</param>
    /// <returns>A semantic compilation containing collection, boundary, and cleanup contracts.</returns>
    static CSharpCompilation CreateCompilation(string consumerSource) {
        string source = """
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
            public sealed class NativeOwnedReturnAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public static class Factory {
                [NativeOwnedReturn]
                public static List<int> Build() {
                    return new List<int>();
                }
            }

            public abstract class Sink {
                public abstract void Take([NativeTakesOwnership] List<int> values);
            }

            public abstract class UnknownSink {
                public abstract void Store(List<int> values);
            }
            """ + Environment.NewLine + consumerSource;
        return OwnershipRoslynTestHelper.CreateCompilation(source);
    }

    /// <summary>
    /// Resolves method summaries and then analyzes local ownership for one compilation.
    /// </summary>
    /// <param name="compilation">Compilation to analyze.</param>
    /// <returns>The local ownership result and emission plan.</returns>
    static CPPOwnershipAnalysisResult Analyze(CSharpCompilation compilation) {
        CPPMethodOwnershipSummaryResolution summaries = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);
        return new CPPLocalOwnershipAnalyzer().Analyze([compilation], summaries);
    }

    /// <summary>
    /// Resolves one local plan by source variable name.
    /// </summary>
    /// <param name="compilation">Compilation containing the local declaration.</param>
    /// <param name="result">Completed local ownership analysis.</param>
    /// <param name="localName">Source local name to select.</param>
    /// <returns>The emission plan for the selected local.</returns>
    static CPPLocalOwnershipPlan ResolveLocalPlan(
        CSharpCompilation compilation,
        CPPOwnershipAnalysisResult result,
        string localName) {
        VariableDeclaratorSyntax declaration = compilation.SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(candidate => string.Equals(candidate.Identifier.Text, localName, StringComparison.Ordinal));
        Assert.True(result.EmissionPlan.TryGetLocalPlan(declaration, out CPPLocalOwnershipPlan plan));
        return plan;
    }
}
