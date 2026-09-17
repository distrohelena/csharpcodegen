using cs2.cpp;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>
/// Proves per-tree parallel ownership analysis yields the same plan and diagnostics as the sequential walk.
/// </summary>
public sealed class CPPParallelOwnershipAnalysisTests {
    /// <summary>
    /// Several methods across types so more than one syntax tree worth of executables is analyzed.
    /// </summary>
    const string Source = """
        public class First {
            public string Name;
            public First(string name) { Name = name; }
            public string Join(string suffix) { return Name + suffix; }
            public void Require(object value) { object checkedValue = value ?? throw new System.InvalidOperationException("required"); }
        }

        public class Second {
            public int Total;
            public int Add(int amount) { Total += amount; return Total; }
            public static int Twice(int value) { return value * 2; }
            public bool Check(int value) { if (value > 10) { return true; } else if (value < 0) { throw new System.ArgumentException("neg"); } return false; }
        }

        public class Third {
            public string Run() { First first = new First("a"); return first.Join("b") + Second.Twice(2); }
        }
        """;

    /// <summary>
    /// One worker and four workers produce the same transitions, local plans and diagnostics in the same order.
    /// </summary>
    [Fact]
    public void Analyze_WithOneAndFourWorkers_ProducesIdenticalResult() {
        Compilation compilation = RoslynTestHelper.CreateCompilation(Source);
        CPPOwnershipAnalysisCoordinator coordinator = new CPPOwnershipAnalysisCoordinator();

        CPPOwnershipAnalysisResult one = coordinator.Analyze(new[] { compilation }, 1);
        CPPOwnershipAnalysisResult four = coordinator.Analyze(new[] { compilation }, 4);

        Assert.Equal(one.Diagnostics.Select(diagnostic => diagnostic.Message), four.Diagnostics.Select(diagnostic => diagnostic.Message));
        Assert.Equal(DescribeTransitions(one), DescribeTransitions(four));

        IEnumerable<VariableDeclaratorSyntax> declarators = compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>());
        foreach (VariableDeclaratorSyntax declarator in declarators) {
            bool oneHasPlan = one.EmissionPlan.TryGetLocalPlan(declarator, out CPPLocalOwnershipPlan onePlan);
            bool fourHasPlan = four.EmissionPlan.TryGetLocalPlan(declarator, out CPPLocalOwnershipPlan fourPlan);
            Assert.Equal(oneHasPlan, fourHasPlan);
            if (oneHasPlan) {
                Assert.Equal(onePlan.InitialOwnership, fourPlan.InitialOwnership);
                Assert.Equal(onePlan.OwnershipFlagName, fourPlan.OwnershipFlagName);
                Assert.Equal(onePlan.RequiresScopeGuard, fourPlan.RequiresScopeGuard);
                Assert.Equal(onePlan.InitiallyOwnsValue, fourPlan.InitiallyOwnsValue);
            }
        }
    }

    /// <summary>
    /// Many syntax trees so four workers really run concurrently, and every consumer tree reports one hard error.
    /// </summary>
    [Fact]
    public void Analyze_WithManyTreesAndFourWorkers_MergesDiagnosticsInTreeOrder() {
        CSharpCompilation compilation = CreateManyTreeCompilation(ConsumerTreeCount);
        CPPOwnershipAnalysisCoordinator coordinator = new CPPOwnershipAnalysisCoordinator();

        CPPOwnershipAnalysisResult one = coordinator.Analyze(new Compilation[] { compilation }, 1);
        CPPOwnershipAnalysisResult four = coordinator.Analyze(new Compilation[] { compilation }, 4);

        Assert.Equal(
            one.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.FilePath}:{diagnostic.LineNumber}:{diagnostic.Message}"),
            four.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.FilePath}:{diagnostic.LineNumber}:{diagnostic.Message}"));
        Assert.Equal(DescribeSourcedTransitions(one), DescribeSourcedTransitions(four));

        List<string> expectedFilePaths = [];
        for (int index = 0; index < ConsumerTreeCount; index++) {
            expectedFilePaths.Add(ConsumerFilePath(index));
        }

        Assert.Equal(
            expectedFilePaths,
            four.Diagnostics.Where(diagnostic => diagnostic.Code == "CPPOWN004").Select(diagnostic => diagnostic.FilePath).ToList());
    }

    /// <summary>
    /// Number of consumer trees analyzed in the many-tree determinism fixture.
    /// </summary>
    const int ConsumerTreeCount = 12;

    /// <summary>
    /// Shared ownership contract declarations parsed into the first syntax tree of the many-tree fixture.
    /// </summary>
    const string PreludeSource = """
        using System;
        using System.Collections.Generic;

        [AttributeUsage(AttributeTargets.Parameter)]
        public sealed class NativeTakesOwnershipAttribute : Attribute {
        }

        public abstract class Sink {
            public abstract int TakeAndReturn([NativeTakesOwnership] List<int> values);
        }
        """;

    /// <summary>
    /// Two trees that share one source path report their identical diagnostic once, exactly as the sequential walk did.
    /// </summary>
    [Fact]
    public void Analyze_WithSharedFilePathAcrossTrees_ReportsEachDiagnosticOnce() {
        CSharpCompilation shared = CreateSharedPathCompilation(2);
        CSharpCompilation single = CreateSharedPathCompilation(1);
        CPPOwnershipAnalysisCoordinator coordinator = new CPPOwnershipAnalysisCoordinator();

        int singleCount = coordinator.Analyze(new Compilation[] { single }, 1).Diagnostics.Count;
        int sharedOneWorkerCount = coordinator.Analyze(new Compilation[] { shared }, 1).Diagnostics.Count;
        int sharedFourWorkerCount = coordinator.Analyze(new Compilation[] { shared }, 4).Diagnostics.Count;

        Assert.True(singleCount > 0, "The shared-path fixture must produce at least one ownership diagnostic.");
        Assert.Equal(singleCount, sharedOneWorkerCount);
        Assert.Equal(singleCount, sharedFourWorkerCount);
    }

    /// <summary>
    /// Builds one compilation whose consumer trees all carry the same source path and the same diagnostic coordinates.
    /// </summary>
    /// <param name="treeCount">Number of identically pathed consumer trees appended after the shared prelude tree.</param>
    /// <returns>A compilation whose consumer trees are indistinguishable by diagnostic identity.</returns>
    static CSharpCompilation CreateSharedPathCompilation(int treeCount) {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular);
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(PreludeSource, "SharedPathOwnershipFixture", filePath: "Prelude.cs");
        List<SyntaxTree> sharedTrees = [];
        for (int index = 0; index < treeCount; index++) {
            string sharedSource = $$"""
                using System.Collections.Generic;

                public static class Shared{{(char)('A' + index)}} {
                    public static void Run(Sink sink) {
                        List<int> values = new List<int>();
                        Combine(sink.TakeAndReturn(values), values.Count);
                    }

                    static void Combine(int first, int second) {
                    }
                }
                """;
            sharedTrees.Add(CSharpSyntaxTree.ParseText(sharedSource, parseOptions, "Shared.cs"));
        }

        return compilation.AddSyntaxTrees(sharedTrees);
    }

    /// <summary>
    /// Builds one compilation whose consumer trees each declare an owned local and use it after transfer.
    /// </summary>
    /// <param name="consumerTreeCount">Number of consumer trees appended after the shared prelude tree.</param>
    /// <returns>A compilation with one prelude tree and the requested number of consumer trees.</returns>
    static CSharpCompilation CreateManyTreeCompilation(int consumerTreeCount) {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular);
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(PreludeSource, "ParallelOwnershipFixture", filePath: "Prelude.cs");
        List<SyntaxTree> consumerTrees = [];
        for (int index = 0; index < consumerTreeCount; index++) {
            string consumerSource = $$"""
                using System.Collections.Generic;

                public static class Consumer{{index}} {
                    public static void Run(Sink sink) {
                        List<int> values = new List<int>();
                        Combine(sink.TakeAndReturn(values), values.Count);
                    }

                    static void Combine(int first, int second) {
                    }
                }
                """;
            consumerTrees.Add(CSharpSyntaxTree.ParseText(consumerSource, parseOptions, ConsumerFilePath(index)));
        }

        return compilation.AddSyntaxTrees(consumerTrees);
    }

    /// <summary>
    /// Builds the stable source path of one consumer tree so diagnostics can be compared in tree order.
    /// </summary>
    /// <param name="index">Zero-based consumer tree index.</param>
    /// <returns>The source path attached to that consumer tree.</returns>
    static string ConsumerFilePath(int index) {
        return $"Consumer{index:D2}.cs";
    }

    /// <summary>
    /// Renders every transition's identity and outcome in plan order so two runs can be compared as sequences.
    /// </summary>
    /// <param name="result">Analysis result whose ordered transitions are rendered.</param>
    /// <returns>One comparable string per transition in plan order.</returns>
    static IEnumerable<string> DescribeTransitions(CPPOwnershipAnalysisResult result) {
        return result.EmissionPlan.Transitions.Select(transition =>
            $"{transition.Syntax.SpanStart}:{transition.LocalDeclaration?.Identifier.ValueText}:{transition.Kind}:{transition.ResultingOwnership}:{transition.ResultingLifecycle}");
    }

    /// <summary>
    /// Renders every transition with its owning source path so trees with identical shapes stay distinguishable.
    /// </summary>
    /// <param name="result">Analysis result whose ordered transitions are rendered.</param>
    /// <returns>One comparable string per transition in plan order.</returns>
    static IEnumerable<string> DescribeSourcedTransitions(CPPOwnershipAnalysisResult result) {
        return result.EmissionPlan.Transitions.Select(transition =>
            $"{transition.Syntax.SyntaxTree.FilePath}:{transition.Syntax.SpanStart}:{transition.LocalDeclaration?.Identifier.ValueText}:{transition.Kind}:{transition.ResultingOwnership}:{transition.ResultingLifecycle}");
    }
}
