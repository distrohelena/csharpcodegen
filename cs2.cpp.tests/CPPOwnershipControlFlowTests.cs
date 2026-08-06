using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies conservative ownership state propagation across branches, loops, exits, and captures.
/// </summary>
public sealed class CPPOwnershipControlFlowTests {
    /// <summary>
    /// Ensures value-type locals produced by borrowed property access never enter native ownership flow.
    /// </summary>
    [Fact]
    public void Analyze_WithLoopCarriedValueTypePropertyResult_DoesNotReportOwnershipDiagnostics() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(string text) {
                for (int i = 0; i < text.Length; i++) {
                    char character = text[i];
                    Use(character);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        Assert.DoesNotContain(result.EmissionPlan.Transitions, transition => transition.LocalName == "character");
    }

    /// <summary>
    /// Ensures a loop-local borrowed reference may be absent on the entry edge and borrowed on the back edge.
    /// </summary>
    [Fact]
    public void Analyze_WithLoopCarriedBorrowedReference_DoesNotReportAmbiguousJoin() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(List<Sink> values) {
                for (int i = 0; i < values.Count; i++) {
                    Sink value = values[i];
                    Use(i);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN009");
    }

    /// <summary>
    /// Ensures replacing a borrowed local with another borrowed member value preserves borrowed lifecycle state.
    /// </summary>
    [Fact]
    public void Analyze_WithBorrowedLocalReassignment_PreservesBorrowedState() {
        CPPOwnershipAnalysisResult result = Analyze("""
            sealed class Node {
                public Node Parent;
            }

            public static void Run(Node candidate) {
                Node current = candidate;
                while (current != null) {
                    current = current.Parent;
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures an owned local declared inside a loop is represented by its lexical guard rather than merged with the pre-loop edge.
    /// </summary>
    [Fact]
    public void Analyze_WithOwnedLocalDeclaredInsideLoop_DoesNotReportAmbiguousJoin() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(List<Sink> values) {
                for (int i = 0; i < values.Count; i++) {
                    List<int> items = new List<int>();
                    Use(items.Count);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN009");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "items");
    }

    /// <summary>
    /// Ensures an owned loop-local transferred into array storage is reinitialized on each iteration instead of merging with its prior transferred state.
    /// </summary>
    [Fact]
    public void Analyze_WithOwnedLoopLocalTransferredToArrayElement_DoesNotReportAmbiguousJoin() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(byte[][] values) {
                for (int i = 0; i < values.Length; i++) {
                    byte[] data = new byte[4];
                    values[i] = data;
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN009");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "data");
    }

    /// <summary>
    /// Ensures reviewed metadata-only no-escape parameters preserve ownership of caller-created storage.
    /// </summary>
    [Fact]
    public void Analyze_WithFrameworkNoEscapeParameter_KeepsCallerOwnership() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static string Run() {
                List<string> values = new List<string>();
                return string.Join(", ", values);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures every explicitly annotated parameter in a multi-argument call preserves caller ownership independently.
    /// </summary>
    [Fact]
    public void Analyze_WithMultipleNoEscapeParameters_KeepsCallerOwnership() {
        CPPOwnershipAnalysisResult result = Analyze("""
            static void Consume([NativeNoEscape] List<int> first, [NativeNoEscape] List<int> second) {
            }

            public static void Run() {
                List<int> first = new List<int>();
                List<int> second = new List<int>();
                Consume(first, second);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        Assert.Equal(2, result.EmissionPlan.Transitions.Count(transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup));
    }

    /// <summary>
    /// Ensures an owned argument retained only by a returned disposable remains caller-owned when that disposable is bounded by a using declaration.
    /// </summary>
    [Fact]
    public void Analyze_WithBorrowRetainedByUsingResult_KeepsCallerOwnership() {
        CPPOwnershipAnalysisResult result = Analyze("""
            sealed class Reader : IDisposable {
                readonly List<int> Source;

                Reader(List<int> source) {
                    Source = source;
                }

                public static Reader Create(List<int> source) {
                    return new Reader(source);
                }

                public void Dispose() {
                }
            }

            public static void Run() {
                List<int> source = new List<int>();
                using Reader reader = Reader.Create(source);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "source");
    }

    /// <summary>
    /// Ensures identical live-owned branch outputs join without ambiguity.
    /// </summary>
    [Fact]
    public void Analyze_WithOwnedReplacementOnBothBranches_MergesLiveOwned() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool enabled) {
                List<int> values = new List<int>();
                if (enabled) {
                    NativeOwnership.Delete(values);
                    values = new List<int>();
                } else {
                    NativeOwnership.Delete(values);
                    values = new List<int>();
                }
                Use(values.Count);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN009");
        Assert.Equal(2, result.EmissionPlan.Transitions.Count(transition => transition.Kind == CPPOwnershipTransitionKind.Replace));
    }

    /// <summary>
    /// Ensures identical released branch outputs join as released without scope cleanup.
    /// </summary>
    [Fact]
    public void Analyze_WithReleaseOnBothBranches_MergesReleased() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool enabled) {
                List<int> values = new List<int>();
                if (enabled) {
                    NativeOwnership.Delete(values);
                } else {
                    NativeOwnership.Delete(values);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN009");
        Assert.DoesNotContain(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures incompatible live and transferred branch outputs stop ownership analysis at their join.
    /// </summary>
    [Fact]
    public void Analyze_WithLiveAndTransferredBranches_ReportsCPPOWN009() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool enabled, Sink sink) {
                List<int> values = new List<int>();
                if (enabled) {
                    sink.Take(values);
                }
                Use(1);
            }
            """);

        CPPConversionDiagnostic diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN009");
        Assert.Equal(CPPDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.LineNumber > 0);
        Assert.True(diagnostic.ColumnNumber > 0);
    }

    /// <summary>
    /// Ensures loop-carried storage may be replaced after explicit cleanup.
    /// </summary>
    [Fact]
    public void Analyze_WithCleanedLoopReplacement_RecordsReplace() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool enabled) {
                List<int> values = new List<int>();
                while (enabled) {
                    NativeOwnership.Delete(values);
                    values = new List<int>();
                    enabled = false;
                }
                Use(values.Count);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN008");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Replace && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures loop-carried owned storage records structured replacement cleanup for each iteration.
    /// </summary>
    [Fact]
    public void Analyze_WithLoopOwnedReplacement_RecordsReplaceTransition() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool enabled) {
                List<int> values = new List<int>();
                while (enabled) {
                    values = new List<int>();
                    enabled = false;
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN008");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Replace && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures replacing a live owned value with null records the cleanup required to disarm its guard.
    /// </summary>
    [Fact]
    public void Analyze_WithNullReplacement_RecordsRelease() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run() {
                List<int> values = new List<int>();
                values = null;
            }
            """);

        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Release && transition.LocalName == "values");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN008");
    }

    /// <summary>
    /// Ensures early return, throw, and finally edges retain exactly-once guarded scope cleanup.
    /// </summary>
    [Fact]
    public void Analyze_WithEarlyAndExceptionalExits_KeepsScopeCleanup() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool enabled) {
                List<int> values = new List<int>();
                try {
                    if (enabled) {
                        return;
                    }
                    if (values.Count == 0) {
                        throw new InvalidOperationException();
                    }
                } finally {
                    Use(1);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        Assert.Single(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures explicit cleanup in a finally region disarms the scope guard on every exit.
    /// </summary>
    [Fact]
    public void Analyze_WithReleaseInFinally_UsesFinallyState() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool enabled) {
                List<int> values = new List<int>();
                try {
                    if (enabled) {
                        return;
                    }
                    Use(values.Count);
                } finally {
                    NativeOwnership.Delete(values);
                }
            }
            """);

        Assert.DoesNotContain(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Release && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures incompatible states that meet only at the terminal exit do not produce an executable-join error.
    /// </summary>
    [Fact]
    public void Analyze_WithDifferentTerminalReturnStates_DoesNotReportCPPOWN009() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static List<int> Build(bool returnLocal) {
                List<int> values = new List<int>();
                if (returnLocal) {
                    return values;
                }
                return new List<int>();
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN009");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures nested argument calls are analyzed in runtime evaluation order.
    /// </summary>
    [Fact]
    public void Analyze_WithTransferInEarlierArgument_ReportsLaterUseAfterTransfer() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(Sink sink) {
                List<int> values = new List<int>();
                Combine(sink.TakeAndReturn(values), values.Count);
            }

            static void Combine(int first, int second) {
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN004");
    }

    /// <summary>
    /// Ensures each escaping executable capture of a live owned local is rejected rather than guessed safe.
    /// </summary>
    /// <param name="captureStatement">Lambda, delegate, or local-function declaration that captures the local.</param>
    [Theory]
    [InlineData("Action capture = () => Use(values.Count);")]
    [InlineData("Action capture = delegate { Use(values.Count); };")]
    [InlineData("void Capture() { Use(values.Count); }")]
    public void Analyze_WithOwnedLocalCapture_ReportsCPPOWN002(string captureStatement) {
        CPPOwnershipAnalysisResult result = Analyze($$"""
            public static void Run() {
                List<int> values = new List<int>();
                {{captureStatement}}
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002");
    }

    /// <summary>
    /// Ensures a capture created after ownership transfer is not mislabeled as capturing a live owned value.
    /// </summary>
    [Fact]
    public void Analyze_WithCaptureAfterTransfer_DoesNotReportCaptureCPPOWN002() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(Sink sink) {
                List<int> values = new List<int>();
                sink.Take(values);
                Action capture = () => Use(values.Count);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002");
    }

    /// <summary>
    /// Ensures a null local captured before a later owned replacement is not treated as live-owned at capture time.
    /// </summary>
    [Fact]
    public void Analyze_WithCaptureBeforeOwnedReplacement_DoesNotReportCaptureCPPOWN002() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run() {
                List<int> values = null;
                Action capture = () => Use(values.Count);
                values = new List<int>();
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002");
    }

    /// <summary>
    /// Ensures conditional cleanup inside finally does not masquerade as cleanup on every finalization path.
    /// </summary>
    [Fact]
    public void Analyze_WithConditionalFinallyRelease_KeepsScopeCleanup() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool cleanup) {
                List<int> values = new List<int>();
                try {
                    Use(values.Count);
                } finally {
                    if (cleanup) {
                        NativeOwnership.Delete(values);
                    }
                }
            }
            """);

        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures dispose-and-release helpers end a local lifetime exactly like direct native cleanup.
    /// </summary>
    [Fact]
    public void Analyze_WithDisposeAndReleaseLocal_RecordsReleaseWithoutScopeCleanup() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run() {
                List<int> values = new List<int>();
                NativeOwnership.DisposeAndRelease(values);
            }
            """);

        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Release && transition.LocalName == "values");
        Assert.DoesNotContain(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures a transfer on a returning branch does not suppress capture rejection on the live fall-through path.
    /// </summary>
    [Fact]
    public void Analyze_WithNonDominatingTransferBeforeCapture_ReportsCPPOWN002() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool transfer, Sink sink) {
                List<int> values = new List<int>();
                if (transfer) {
                    sink.Take(values);
                    return;
                }
                Action capture = () => Use(values.Count);
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002");
    }

    /// <summary>
    /// Ensures an owned local cannot escape through a constructor parameter without transferring cleanup responsibility.
    /// </summary>
    [Fact]
    public void Analyze_WithEscapingConstructorArgument_ReportsCPPOWN002() {
        CPPOwnershipAnalysisResult result = Analyze("""
            sealed class Asset {
                List<int> Stored;

                public Asset(List<int> values) {
                    Stored = values;
                }
            }

            public static void Run() {
                List<int> values = new List<int>();
                Asset asset = new Asset(values);
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN002" && diagnostic.SourceMemberName == "Run");
    }

    /// <summary>
    /// Ensures an owned local passed to a takes-ownership constructor records a transfer at the object creation site.
    /// </summary>
    [Fact]
    public void Analyze_WithTakesOwnershipConstructorArgument_RecordsTransfer() {
        CPPOwnershipAnalysisResult result = Analyze("""
            sealed class Asset {
                public Asset([NativeTakesOwnership] List<int> values) {
                    NativeOwnership.Delete(values);
                }
            }

            public static void Run() {
                List<int> values = new List<int>();
                Asset asset = new Asset(values);
            }
            """);

        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "values");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures a retained-borrow constructor can store an owner back-reference without consuming the owner's lifetime.
    /// </summary>
    [Fact]
    public void Analyze_WithRetainsBorrowConstructorArgument_PreservesOwnerLifetime() {
        CPPOwnershipAnalysisResult result = Analyze("""
            sealed class Owner {
            }

            sealed class Child {
                Owner OwnerValue;

                public Child([NativeRetainsBorrow] Owner owner) {
                    OwnerValue = owner;
                }
            }

            public static Owner Run() {
                Owner owner = new Owner();
                Child child = new Child(owner);
                return owner;
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        CPPOwnershipTransition transfer = Assert.Single(
            result.EmissionPlan.Transitions,
            transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "owner");
        Assert.IsType<Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax>(transfer.Syntax);
    }

    /// <summary>
    /// Ensures one owned local cannot satisfy two takes-ownership parameters in the same call.
    /// </summary>
    [Fact]
    public void Analyze_WithDuplicateTakesOwnershipArguments_ReportsCPPOWN004() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Consume(
                [NativeTakesOwnership] List<int> first,
                [NativeTakesOwnership] List<int> second) {
                NativeOwnership.Delete(first);
                NativeOwnership.Delete(second);
            }

            public static void Run() {
                List<int> values = new List<int>();
                Consume(values, values);
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN004" && diagnostic.SourceMemberName == "Run");
    }

    /// <summary>
    /// Ensures an owned local inserted into a collection transfers ownership so the scope-end delete is disarmed instead of dangling the entry.
    /// </summary>
    [Fact]
    public void Analyze_WithOwnedLocalAddedToCollection_TransfersOwnershipWithoutDiagnostics() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(List<List<int>> records) {
                List<int> record = new List<int>();
                records.Add(record);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "record");
    }

    /// <summary>
    /// Ensures a fresh allocation passed directly into a collection insertion stays diagnostic-free.
    /// </summary>
    [Fact]
    public void Analyze_WithFreshAllocationAddedToCollection_DoesNotReportDiagnostics() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(List<List<int>> records) {
                records.Add(new List<int>());
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures a borrowed local inserted into a collection neither transfers ownership nor reports diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_WithBorrowedLocalAddedToCollection_KeepsBorrowedStateWithoutDiagnostics() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(List<List<int>> records, List<int> element) {
                List<int> borrowed = element;
                records.Add(borrowed);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
        Assert.DoesNotContain(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "borrowed");
    }

    /// <summary>
    /// Ensures inserting the same owned local into two collections reports the double-transfer ambiguity.
    /// </summary>
    [Fact]
    public void Analyze_WithOwnedLocalAddedToTwoCollections_ReportsCPPOWN004() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(List<List<int>> first, List<List<int>> second) {
                List<int> record = new List<int>();
                first.Add(record);
                second.Add(record);
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN004" && diagnostic.SourceMemberName == "Run");
    }

    /// <summary>
    /// Ensures an explicitly annotated retains-borrow parameter stays diagnostic-free because the annotation is a reviewed lifetime contract.
    /// </summary>
    [Fact]
    public void Analyze_WithOwnedLocalPassedToAnnotatedRetainsBorrowParameter_DoesNotReportDiagnostics() {
        CPPOwnershipAnalysisResult result = Analyze("""
            static void Retain([NativeRetainsBorrow] List<int> value) {
            }

            public static void Run() {
                List<int> value = new List<int>();
                Retain(value);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CPPOWN", StringComparison.Ordinal));
    }

    /// <summary>
    /// Builds one ownership fixture and analyzes its consumer method.
    /// </summary>
    /// <param name="methodSource">Consumer method declaration.</param>
    /// <returns>The complete semantic ownership result.</returns>
    static CPPOwnershipAnalysisResult Analyze(string methodSource) {
        string source = """
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeNoEscapeAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeRetainsBorrowAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }

                public static void DisposeAndRelease<T>(T value) where T : class {
                }
            }

            public abstract class Sink {
                public abstract void Take([NativeTakesOwnership] List<int> values);

                public abstract int TakeAndReturn([NativeTakesOwnership] List<int> values);
            }

            public static class Consumer {
                static void Use(int value) {
                }
            """ + Environment.NewLine + methodSource + Environment.NewLine + "}";
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation(source);
        CPPMethodOwnershipSummaryResolution summaries = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);
        return new CPPLocalOwnershipAnalyzer().Analyze([compilation], summaries);
    }
}
