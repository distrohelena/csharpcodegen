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
    /// One worker, four workers, four workers again, and sixteen workers produce identical generated text and report state; sixteen exceeds the class count, so the pool caps the threads it starts.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOneAndFourWorkers_ProducesIdenticalOutput() {
        CPPOwnershipConversionTestWorkspace workspace = new CPPOwnershipConversionTestWorkspace();
        using CPPOwnershipConversionOutput one = workspace.Convert("emission-determinism-one", Source, Workers("1"));
        using CPPOwnershipConversionOutput four = workspace.Convert("emission-determinism-four", Source, Workers("4"));
        using CPPOwnershipConversionOutput fourAgain = workspace.Convert("emission-determinism-four-again", Source, Workers("4"));
        using CPPOwnershipConversionOutput sixteen = workspace.Convert("emission-determinism-sixteen", Source, Workers("16"));

        Assert.False(one.Report.HasErrors);
        Assert.Equal(4, one.Report.EmittedTypeCount);
        Assert.Equal(one.GeneratedText, four.GeneratedText);
        Assert.Equal(four.GeneratedText, fourAgain.GeneratedText);
        Assert.Equal(one.GeneratedText, sixteen.GeneratedText);
        Assert.Equal(one.Report.RegisteredRuntimeRequirements, four.Report.RegisteredRuntimeRequirements);
        Assert.Equal(one.Report.Diagnostics.Count, four.Report.Diagnostics.Count);
        Assert.Equal(one.Report.EmittedTypeCount, four.Report.EmittedTypeCount);
        Assert.Equal(
            one.Report.EmittedFiles.Select(Path.GetFileName),
            four.Report.EmittedFiles.Select(Path.GetFileName));
    }

    /// <summary>
    /// Fixture whose middle class trips a CPP1001 runtime capability violation, with reachable classes ordered before and after it so an abort leaves later classes in flight on other workers.
    /// </summary>
    const string AbortSource = """
        /// <summary>First reachable class, lowered before the failing one.</summary>
        public class Aardvark {
            /// <summary>Returns a constant.</summary>
            public int Value() {
                return 1;
            }
        }

        /// <summary>Second reachable class, lowered before the failing one.</summary>
        public class Beacon {
            /// <summary>Returns a constant.</summary>
            public int Value() {
                return 2;
            }
        }

        /// <summary>Third reachable class, lowered before the failing one.</summary>
        public class Cobalt {
            /// <summary>Returns a constant.</summary>
            public int Value() {
                return 3;
            }
        }

        /// <summary>Failing class whose try/catch needs exception unwinding.</summary>
        public class Dynamo {
            /// <summary>Uses a construct the disabled exception capability cannot lower.</summary>
            public void Run() {
                try {
                    throw new System.Exception();
                } catch (System.Exception) {
                }
            }
        }

        /// <summary>First reachable class after the failing one.</summary>
        public class Emerald {
            /// <summary>Returns a constant.</summary>
            public int Value() {
                return 5;
            }
        }

        /// <summary>Second reachable class after the failing one.</summary>
        public class Fathom {
            /// <summary>Returns a constant.</summary>
            public int Value() {
                return 6;
            }
        }

        /// <summary>Third reachable class after the failing one.</summary>
        public class Granite {
            /// <summary>Returns a constant.</summary>
            public int Value() {
                return 7;
            }
        }
        """;

    /// <summary>
    /// An aborted emission pass throws the same runtime capability violation and reports errors at one and four workers, because the pool always rethrows the failure of the lowest reachability index.
    /// </summary>
    /// <remarks>
    /// Both conversions share one source file so the diagnostic file path embedded in the message is identical; only the output directory and the worker count differ. The diagnostic count is deliberately not compared, because a cooperative abort lets later classes complete on other workers and contribute diagnostics a single worker never reaches.
    /// </remarks>
    [Fact]
    public void WriteOutput_WithAbortingClass_ThrowsTheSameFailureAtOneAndFourWorkers() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-parallel-abort-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        Directory.CreateDirectory(rootPath);
        try {
            File.WriteAllText(projectPath, CreateAbortProjectFile());
            File.WriteAllText(Path.Combine(rootPath, "Fixture.cs"), AbortSource);

            NotSupportedException oneWorkerFailure = ConvertExpectingAbort(projectPath, Path.Combine(rootPath, "out-one"), "1", out bool oneWorkerHasErrors);
            NotSupportedException fourWorkerFailure = ConvertExpectingAbort(projectPath, Path.Combine(rootPath, "out-four"), "4", out bool fourWorkerHasErrors);

            Assert.StartsWith("CPP1001 ", oneWorkerFailure.Message);
            Assert.Contains("Dynamo.Run", oneWorkerFailure.Message);
            Assert.Equal(oneWorkerFailure.Message, fourWorkerFailure.Message);
            Assert.True(oneWorkerHasErrors);
            Assert.True(fourWorkerHasErrors);
        } finally {
            Directory.Delete(rootPath, true);
        }
    }

    /// <summary>
    /// Converts the abort fixture with a fixed worker count and returns the runtime capability violation it must raise.
    /// </summary>
    /// <param name="projectPath">Project file shared by every worker count so diagnostic paths match.</param>
    /// <param name="outputPath">Directory that receives the partial generated output.</param>
    /// <param name="workerCount">Worker thread count written into the codegen option table.</param>
    /// <param name="hasErrors">Receives whether the converter report ended up holding errors.</param>
    /// <returns>The runtime capability violation raised by the aborted pass.</returns>
    static NotSupportedException ConvertExpectingAbort(string projectPath, string outputPath, string workerCount, out bool hasErrors) {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.UseExceptions] = bool.FalseString,
            [CPPCodegenOptionNames.UseRtti] = bool.TrueString,
            [CPPCodegenOptionNames.WorkerThreads] = workerCount
        };

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => converter.WriteOutput(outputPath));
        hasErrors = converter.Report.HasErrors;
        return failure;
    }

    /// <summary>
    /// Creates the minimal SDK project used by the abort fixture.
    /// </summary>
    /// <returns>Complete SDK project XML.</returns>
    static string CreateAbortProjectFile() {
        return """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """;
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
