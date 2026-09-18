using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Proves parallel class emission produces the same bytes and report as a single worker.
/// </summary>
public sealed class CPPParallelEmissionDeterminismTests {
    /// <summary>
    /// Fixture with cross-class references, string switches, a generic, and per-class lowering temporaries minted by the <c>Scoped()</c> methods, whose local <c>new Box&lt;int&gt;</c> is what forces a temporary in both <c>Alpha</c> and <c>Beta</c>.
    /// </summary>
    const string Source = """
        /// <summary>Holds a name and exercises temporaries.</summary>
        public class Alpha {
            /// <summary>Stored name.</summary>
            public string Name;

            /// <summary>Initializes the instance.</summary>
            public Alpha(string name) {
                Name = name;
            }

            /// <summary>Requires a value and formats it.</summary>
            public string Describe(object value) {
                object checkedValue = value ?? throw new System.InvalidOperationException("required");
                return $"{Name}:{Beta.Twice(3)}";
            }

            /// <summary>Creates a scoped instance so this class mints a lowering temporary.</summary>
            public int Scoped() {
                Box<int> box = new Box<int>(1);
                return box.Value;
            }

            /// <summary>Exercises a string switch.</summary>
            public int Classify(string value) {
                return value switch {
                    "x" => 1,
                    _ => 0
                };
            }
        }

        /// <summary>Static helpers referenced by other classes.</summary>
        public static class Beta {
            /// <summary>Doubles a value.</summary>
            public static int Twice(int value) {
                return value * 2;
            }

            /// <summary>Exercises checked arithmetic temporaries.</summary>
            public static int Sum(int left, int right) {
                checked {
                    return left + right;
                }
            }

            /// <summary>Creates a scoped instance so this class also mints a lowering temporary.</summary>
            public static int Scoped() {
                Box<int> box = new Box<int>(2);
                return box.Value;
            }
        }

        /// <summary>Generic wrapper referenced with two arities.</summary>
        public class Box<T> {
            /// <summary>Stored value.</summary>
            public T Value;

            /// <summary>Initializes the box.</summary>
            public Box(T value) {
                Value = value;
            }
        }

        /// <summary>Consumer of the other types.</summary>
        public class Gamma {
            /// <summary>Uses every other type so includes and references cross class boundaries.</summary>
            public string Run() {
                Alpha alpha = new Alpha("a");
                Box<int> box = new Box<int>(Beta.Sum(1, 2));
                string result = alpha.Describe(alpha) + box.Value + alpha.Scoped() + Beta.Scoped();
                for (int index = 0; index < 3; index++) {
                    result += alpha.Classify("x");
                }
                return result;
            }
        }
        """;

    /// <summary>
    /// Builds the option override selecting a worker count.
    /// </summary>
    /// <param name="count">Worker thread count written into the codegen option table.</param>
    /// <returns>A platform option override table selecting the worker count.</returns>
    static IReadOnlyDictionary<string, string> Workers(string count) {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { CPPCodegenOptionNames.WorkerThreads, count }
        };
    }

    /// <summary>
    /// One worker, four workers, and four workers again produce identical generated text and report state.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOneAndFourWorkers_ProducesIdenticalOutput() {
        CPPOwnershipConversionTestWorkspace workspace = new CPPOwnershipConversionTestWorkspace();
        using CPPOwnershipConversionOutput one = workspace.Convert("emission-determinism-one", Source, Workers("1"));
        using CPPOwnershipConversionOutput four = workspace.Convert("emission-determinism-four", Source, Workers("4"));
        using CPPOwnershipConversionOutput fourAgain = workspace.Convert("emission-determinism-four-again", Source, Workers("4"));

        Assert.False(one.Report.HasErrors);
        Assert.Equal(4, one.Report.EmittedTypeCount);
        Assert.Equal(one.GeneratedText, four.GeneratedText);
        Assert.Equal(four.GeneratedText, fourAgain.GeneratedText);
        Assert.Equal(one.Report.RegisteredRuntimeRequirements, four.Report.RegisteredRuntimeRequirements);
        Assert.Equal(one.Report.Diagnostics.Count, four.Report.Diagnostics.Count);
        Assert.Equal(one.Report.EmittedTypeCount, four.Report.EmittedTypeCount);
        Assert.Equal(
            one.Report.EmittedFiles.Select(Path.GetFileName),
            four.Report.EmittedFiles.Select(Path.GetFileName));
    }

    /// <summary>
    /// Every class restarts the temporary-name counter at zero, so two classes lowered on different workers both mint a first temporary.
    /// </summary>
    [Fact]
    public void WriteOutput_RestartsTemporaryNamesPerClass() {
        CPPOwnershipConversionTestWorkspace workspace = new CPPOwnershipConversionTestWorkspace();
        using CPPOwnershipConversionOutput output = workspace.Convert("emission-determinism-temporaries", Source, Workers("4"));

        Assert.False(output.Report.HasErrors);
        Assert.Contains("_00000000", File.ReadAllText(Path.Combine(output.OutputPath, "Alpha.cpp")));
        Assert.Contains("_00000000", File.ReadAllText(Path.Combine(output.OutputPath, "Beta.cpp")));
    }
}
