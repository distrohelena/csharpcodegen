using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that source-owned members prove allocation, replacement cleanup, and final disposal.
/// </summary>
public sealed class CPPOwnedMemberContractValidatorTests {
    /// <summary>
    /// Ensures a fully initialized, safely replaced, and disposed owned member is accepted.
    /// </summary>
    [Fact]
    public void Validate_WithCompleteOwnedMemberLifecycle_Succeeds() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Replace() {
                    NativeOwnership.Delete(Stored);
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" || diagnostic.Code == "CPPOWN007");
    }

    /// <summary>
    /// Ensures replacing an owned member without releasing its prior value is rejected.
    /// </summary>
    [Fact]
    public void Validate_WithMissingReplacementCleanup_ReportsCPPOWN007() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Replace() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN007" && diagnostic.SourceMemberName == "Replace");
    }

    /// <summary>
    /// Ensures an owned member must be released on normal disposal exits.
    /// </summary>
    [Fact]
    public void Validate_WithMissingDisposeCleanup_ReportsCPPOWN007() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN007" && diagnostic.SourceMemberName == "Dispose");
    }

    /// <summary>
    /// Ensures a borrowed value cannot satisfy an owned-member assignment contract.
    /// </summary>
    [Fact]
    public void Validate_WithBorrowedOwnedMemberAssignment_ReportsCPPOWN006() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer(List<int> shared) {
                    Stored = shared;
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        CPPConversionDiagnostic diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006");
        Assert.Equal(CPPDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.LineNumber > 0);
        Assert.True(diagnostic.ColumnNumber > 0);
    }

    /// <summary>
    /// Ensures assigning an owned local into an owned member transfers and disarms local cleanup.
    /// </summary>
    [Fact]
    public void Validate_WithOwnedLocalMemberAssignment_TransfersLocalOwnership() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer() {
                    List<int> values = new List<int>();
                    Stored = values;
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "values");
        Assert.DoesNotContain(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures a borrowed local is not reclassified from an owned assignment that occurs later in the method.
    /// </summary>
    [Fact]
    public void Validate_WithBorrowedLocalBeforeLaterOwnedReplacement_ReportsCPPOWN006() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer(List<int> shared) {
                    List<int> values = shared;
                    Stored = values;
                    values = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006");
    }

    /// <summary>
    /// Ensures assignments from another type are still validated against the owned-member contract.
    /// </summary>
    [Fact]
    public void Validate_WithExternalBorrowedAssignment_ReportsCPPOWN006() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                public List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }

            public static class ExternalWriter {
                public static void Replace(Consumer consumer, List<int> shared) {
                    consumer.Stored = shared;
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" && diagnostic.SourceMemberName == "Replace");
    }

    /// <summary>
    /// Ensures existing dispose-and-release helpers satisfy owned-member cleanup proofs.
    /// </summary>
    [Fact]
    public void Validate_WithDisposeAndReleaseHelper_Succeeds() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.DisposeAndRelease(Stored);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN007");
    }

    /// <summary>
    /// Ensures disposal cleanup performed in a finally region applies to every normal exit edge.
    /// </summary>
    [Fact]
    public void Validate_WithDisposeCleanupInFinally_Succeeds() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    try {
                        GC.KeepAlive(Stored);
                    } finally {
                        NativeOwnership.Delete(Stored);
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN007");
    }

    /// <summary>
    /// Ensures expression-bodied external writers cannot bypass owned-member assignment validation.
    /// </summary>
    [Fact]
    public void Validate_WithExpressionBodiedExternalAssignment_ReportsCPPOWN006() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                public List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }

            public static class ExternalWriter {
                public static void Replace(Consumer consumer, List<int> shared) => consumer.Stored = shared;
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" && diagnostic.SourceMemberName == "Replace");
    }

    /// <summary>
    /// Ensures property accessors cannot assign borrowed values into owned members.
    /// </summary>
    [Fact]
    public void Validate_WithAccessorBorrowedAssignment_ReportsCPPOWN006() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public List<int> Input {
                    set {
                        Stored = value;
                    }
                }

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" && diagnostic.SourceMemberName == "set_Input");
    }

    /// <summary>
    /// Ensures a borrowed field initializer cannot establish an owned-member lifetime.
    /// </summary>
    [Fact]
    public void Validate_WithBorrowedOwnedMemberInitializer_ReportsCPPOWN006() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                static List<int> Shared = new List<int>();

                [NativeOwnedMember]
                List<int> Stored = Shared;

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" && diagnostic.SourceMemberName == "Stored");
    }

    /// <summary>
    /// Ensures a constructor cannot overwrite an owned member initializer without cleanup.
    /// </summary>
    [Fact]
    public void Validate_WithOwnedInitializerThenConstructorOverwrite_ReportsCPPOWN007() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored = new List<int>();

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN007" && diagnostic.SourceMemberName == ".ctor");
    }

    /// <summary>
    /// Ensures conditional cleanup in Dispose does not prove cleanup on the branch that skips release.
    /// </summary>
    [Fact]
    public void Validate_WithConditionalFinallyDisposeCleanup_ReportsCPPOWN007() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                readonly bool Cleanup;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    try {
                        GC.KeepAlive(Stored);
                    } finally {
                        if (Cleanup) {
                            NativeOwnership.Delete(Stored);
                        }
                    }
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN007" && diagnostic.SourceMemberName == "Dispose");
    }

    /// <summary>
    /// Ensures a delegating constructor treats the value established by its target constructor as live.
    /// </summary>
    [Fact]
    public void Validate_WithDelegatingConstructorOverwrite_ReportsCPPOWN007() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public Consumer(int value) : this() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN007" && diagnostic.SourceMemberName == ".ctor");
    }

    /// <summary>
    /// Ensures conversion-wrapped owned locals retain their exact transfer proof at member assignment.
    /// </summary>
    [Fact]
    public void Validate_WithConvertedOwnedLocalAssignment_Succeeds() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                IList<int> Stored;

                public Consumer() {
                    List<int> values = new List<int>();
                    Stored = (IList<int>)values;
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "values");
    }

    /// <summary>
    /// Ensures owned-member writes from another participating compilation retain the metadata ownership contract.
    /// </summary>
    [Fact]
    public void Validate_WithBorrowedWriterInReferencedCompilation_ReportsCPPOWN006() {
        CSharpCompilation ownerCompilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                public List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """, "Owner.cs", "Owner");
        CSharpCompilation writerCompilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System.Collections.Generic;

            public static class Writer {
                public static void Replace(Consumer consumer, List<int> shared) {
                    consumer.Stored = shared;
                }
            }
            """, "Writer.cs", "Writer").AddReferences(ownerCompilation.ToMetadataReference());
        CPPMethodOwnershipSummaryResolution summaries = new CPPMethodOwnershipSummaryResolver().Resolve([
            ownerCompilation,
            writerCompilation
        ]);

        CPPOwnershipAnalysisResult result = new CPPLocalOwnershipAnalyzer().Analyze([
            ownerCompilation,
            writerCompilation
        ], summaries);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006" && diagnostic.SourceMemberName == "Replace");
    }

    /// <summary>
    /// Ensures a conditional release inside finally cannot prove cleanup before an unconditional replacement.
    /// </summary>
    [Fact]
    public void Validate_WithConditionalFinallyReleaseBeforeReplacement_ReportsReplacementCPPOWN007() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                List<int> Stored;

                readonly bool Cleanup;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    try {
                        GC.KeepAlive(Stored);
                    } finally {
                        if (Cleanup) {
                            NativeOwnership.Delete(Stored);
                        }
                        Stored = new List<int>();
                    }
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "CPPOWN007" &&
            diagnostic.Message.Contains("replaced before", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures an owned replacement from another compilation still proves cleanup of the prior member value.
    /// </summary>
    [Fact]
    public void Validate_WithOwnedWriterInReferencedCompilation_ReportsCPPOWN007() {
        CSharpCompilation ownerCompilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                public List<int> Stored;

                public Consumer() {
                    Stored = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """, "Owner.cs", "Owner");
        CSharpCompilation writerCompilation = OwnershipRoslynTestHelper.CreateCompilation("""
            using System.Collections.Generic;

            public static class Writer {
                public static void Replace(Consumer consumer) {
                    consumer.Stored = new List<int>();
                }
            }
            """, "Writer.cs", "Writer").AddReferences(ownerCompilation.ToMetadataReference());
        CPPMethodOwnershipSummaryResolution summaries = new CPPMethodOwnershipSummaryResolver().Resolve([
            ownerCompilation,
            writerCompilation
        ]);

        CPPOwnershipAnalysisResult result = new CPPLocalOwnershipAnalyzer().Analyze([
            ownerCompilation,
            writerCompilation
        ], summaries);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN007" && diagnostic.SourceMemberName == "Replace");
    }

    /// <summary>
    /// Ensures alternating parentheses and conversions preserve an owned local's transfer proof.
    /// </summary>
    [Fact]
    public void Validate_WithParenthesizedConvertedOwnedLocalAssignment_Succeeds() {
        CPPOwnershipAnalysisResult result = Analyze("""
            public sealed class Consumer : IDisposable {
                [NativeOwnedMember]
                IList<int> Stored;

                public Consumer() {
                    List<int> values = new List<int>();
                    Stored = (((IList<int>)(values)));
                }

                public void Dispose() {
                    NativeOwnership.Delete(Stored);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CPPOWN006");
        Assert.Contains(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.Transfer && transition.LocalName == "values");
        Assert.DoesNotContain(result.EmissionPlan.Transitions, transition => transition.Kind == CPPOwnershipTransitionKind.ScopeCleanup && transition.LocalName == "values");
    }

    /// <summary>
    /// Builds one owned-member fixture and runs local plus member ownership validation.
    /// </summary>
    /// <param name="consumerSource">Consumer type under validation.</param>
    /// <returns>The complete semantic ownership result.</returns>
    static CPPOwnershipAnalysisResult Analyze(string consumerSource) {
        string source = """
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }

                public static void DisposeAndRelease<T>(T value) where T : class {
                }
            }
            """ + Environment.NewLine + consumerSource;
        CSharpCompilation compilation = OwnershipRoslynTestHelper.CreateCompilation(source);
        CPPMethodOwnershipSummaryResolution summaries = new CPPMethodOwnershipSummaryResolver().Resolve([compilation]);
        return new CPPLocalOwnershipAnalyzer().Analyze([compilation], summaries);
    }
}
