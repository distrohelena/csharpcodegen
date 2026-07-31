using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies conservative ownership state propagation across branches, loops, exits, and captures.
/// </summary>
public sealed class CPPOwnershipControlFlowTests {
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
    /// Ensures loop-carried owned storage cannot be overwritten without first destroying its previous value.
    /// </summary>
    [Fact]
    public void Analyze_WithUncleanLoopOverwrite_ReportsCPPOWN008() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public static void Run(bool enabled) {
                List<int> values = new List<int>();
                while (enabled) {
                    values = new List<int>();
                    enabled = false;
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN008");
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
