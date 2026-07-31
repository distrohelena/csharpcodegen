using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Verifies that the C++ converter preserves checked-context block bodies without leaving unsupported diagnostics behind.
    /// </summary>
    public class CPPUncheckedStatementAuditTests {
        /// <summary>
        /// Ensures an unchecked hash-code block lowers by emitting only the enclosed statements.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUncheckedBlock_DoesNotReportUncheckedStatement() {
            string source = """
                public class HashCarrier {
                    public int GetHashCode(int value, int other) {
                        unchecked {
                            var hashCode = value;
                            hashCode = (hashCode * 397) ^ other;
                            return hashCode;
                        }
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            AssertNoDiagnostic(report, "UncheckedStatement");
            Assert.Contains("hashCode = (hashCode * 397) ^ other;", output);
            Assert.Contains("return hashCode;", output);
        }

        /// <summary>
        /// Ensures a checked counter block routes its mutation through an overflow-checking native helper.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCheckedBlock_DoesNotReportCheckedStatement() {
            string source = """
                public class Counter {
                    long Value { get; set; }
                    int Total { get; set; }

                    public void Increment(int amount) {
                        checked {
                            Value++;
                            Total += amount;
                        }
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            AssertNoDiagnostic(report, "CheckedStatement");
            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            Assert.Contains("Number::CheckedPostIncrement(this->Value);", output);
            Assert.Contains("auto& __checked_target_00000000 = this->Total;", output);
            Assert.Contains("const auto __checked_value_00000001 = amount;", output);
            Assert.Contains("Number::CheckedAddAssign(__checked_target_00000000, __checked_value_00000001);", output);
            Assert.Contains("static T CheckedPostIncrement(T& value)", output);
            Assert.Contains("static T CheckedAddAssign(T& left, const T& right)", output);
            Assert.Contains("if (value == std::numeric_limits<T>::max())", output);
            Assert.Contains("throw OverflowException();", output);
            Assert.DoesNotContain("this->Value++;", output, StringComparison.Ordinal);
            Assert.DoesNotContain("this->Total += amount;", output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures unsupported checked arithmetic remains an explicit conversion error instead of silently using ordinary C++ overflow behavior.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUnsupportedCheckedSubtraction_ReportsCheckedArithmeticDiagnostic() {
            string source = """
                public class Counter {
                    public int Add(int value, int increment) {
                        checked {
                            return value - increment;
                        }
                    }
                }
                """;

            RunConversion(source, out JsonDocument report);

            Assert.True(report.RootElement.GetProperty("hasErrors").GetBoolean());
            AssertDiagnostic(report, "SubtractExpression");
        }

        /// <summary>
        /// Ensures a nested unchecked expression overrides an enclosing checked block instead of inheriting its overflow policy.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUncheckedExpressionInsideCheckedBlock_UsesOrdinaryMutation() {
            string source = """
                public class Counter {
                    int Value;

                    public int Increment() {
                        checked {
                            return unchecked(Value++);
                        }
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            Assert.Contains("this->Value++", output);
            Assert.DoesNotContain("CheckedPostIncrement(this->Value)", output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures checked addition expressions lower through an overflow-preserving native helper.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCheckedExpressionAddition_UsesCheckedAddHelper() {
            string source = """
                public class Counter {
                    public int Add(int value, int increment) {
                        return checked(value + increment);
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            Assert.Contains("Number::CheckedAdd(value, increment)", output);
            Assert.DoesNotContain("__checked_left_", output, StringComparison.Ordinal);
            Assert.DoesNotContain("__checked_right_", output, StringComparison.Ordinal);
            Assert.Contains("static T CheckedAdd(const T& left, const T& right)", output);
        }

        /// <summary>
        /// Ensures checked integral casts validate representability before applying the native conversion.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCheckedIntegralCast_UsesCheckedCastHelper() {
            string source = """
                public class IndexReader {
                    public int Read(uint value, int index) {
                        return checked((int)(value + (uint)index));
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            Assert.Contains("Number::CheckedCast<int32_t>", output);
            Assert.Contains("static TTarget CheckedCast(const TSource& value)", output);
        }

        /// <summary>
        /// Ensures a side-effect-free checked addition nested inside an invocation remains self-contained when its parent cannot host statement temporaries.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedPureCheckedAddition_EmitsInlineCheckedHelper() {
            string source = """
                public class IndexReader {
                    static uint ReadSource(int index) {
                        return (uint)index;
                    }

                    public int Read(int index) {
                        return checked((int)ReadSource(index + 1));
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            Assert.Contains("Number::CheckedAdd(index, 1)", output);
            Assert.DoesNotContain("__checked_left_", output, StringComparison.Ordinal);
            Assert.DoesNotContain("__checked_right_", output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures a checked cast propagates the sequencing required by a side-effecting nested checked addition.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCheckedCastAroundSideEffectingAddition_PropagatesOperandCaptures() {
            string source = """
                public class Counter {
                    int Next() {
                        return 1;
                    }

                    public long Read() {
                        return checked((long)(Next() + 1));
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            int leftCaptureIndex = output.IndexOf("const auto __checked_left_", StringComparison.Ordinal);
            int helperIndex = output.IndexOf("Number::CheckedAdd(__checked_left_", StringComparison.Ordinal);
            int castIndex = output.IndexOf("Number::CheckedCast<int64_t>", StringComparison.Ordinal);
            Assert.True(leftCaptureIndex >= 0 && leftCaptureIndex < castIndex);
            Assert.True(castIndex < helperIndex);
        }

        /// <summary>
        /// Ensures invocation sequencing evaluates a side-effecting receiver and preceding argument before a later checked argument capture.
        /// </summary>
        [Fact]
        public void WriteOutput_WithLaterCheckedInvocationArgument_PreservesReceiverAndArgumentOrder() {
            string source = """
                public class Receiver {
                    public int Mix(int first, int second) {
                        return first + second;
                    }
                }

                public class Counter {
                    Receiver Current = new Receiver();

                    Receiver GetReceiver() {
                        return Current;
                    }

                    int First() {
                        return 1;
                    }

                    int Second() {
                        return 2;
                    }

                    public int Read() {
                        return GetReceiver().Mix(First(), checked(Second() + 1));
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            int receiverIndex = output.IndexOf("this->GetReceiver()", StringComparison.Ordinal);
            int firstIndex = output.IndexOf("this->First()", StringComparison.Ordinal);
            int secondIndex = output.IndexOf("this->Second()", StringComparison.Ordinal);
            int helperIndex = output.IndexOf("Number::CheckedAdd(__checked_left_", StringComparison.Ordinal);
            Assert.True(receiverIndex >= 0 && receiverIndex < firstIndex);
            Assert.True(firstIndex < secondIndex);
            Assert.True(secondIndex < helperIndex);
        }

        /// <summary>
        /// Ensures delegate invocation evaluates the delegate target and earlier arguments before preparing a later checked argument.
        /// </summary>
        [Fact]
        public void WriteOutput_WithLaterCheckedDelegateArgument_PreservesTargetAndArgumentOrder() {
            string source = """
                public delegate int Mixer(int first, int second);

                public class Counter {
                    Mixer Current;

                    int First() {
                        return 1;
                    }

                    int Second() {
                        return 2;
                    }

                    public int Read() {
                        return Current(First(), checked(Second() + 1));
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            int targetIndex = output.IndexOf("auto __invoke_delegate_", StringComparison.Ordinal);
            int firstIndex = output.IndexOf("this->First()", StringComparison.Ordinal);
            int secondIndex = output.IndexOf("this->Second()", StringComparison.Ordinal);
            int helperIndex = output.IndexOf("Number::CheckedAdd(__checked_left_", StringComparison.Ordinal);
            Assert.True(targetIndex >= 0 && targetIndex < firstIndex);
            Assert.True(firstIndex < secondIndex);
            Assert.True(secondIndex < helperIndex);
        }

        /// <summary>
        /// Ensures a delegate target is snapshotted before an argument replaces the source delegate variable.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMutatingDelegateArgument_CapturesOriginalDelegateValue() {
            string source = """
                public delegate int Mixer(int first, int second);

                public class Counter {
                    Mixer Current;

                    static int Replacement(int first, int second) {
                        return first - second;
                    }

                    int ReplaceCurrent() {
                        Current = Replacement;
                        return 1;
                    }

                    int Second() {
                        return 2;
                    }

                    public int Read() {
                        return Current(ReplaceCurrent(), checked(Second() + 1));
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            Assert.Contains("auto __invoke_delegate_", output, StringComparison.Ordinal);
            Assert.DoesNotContain("auto&& __invoke_delegate_", output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures reduced extension invocation evaluates its receiver and earlier arguments before preparing a later checked argument.
        /// </summary>
        [Fact]
        public void WriteOutput_WithLaterCheckedReducedExtensionArgument_PreservesReceiverAndArgumentOrder() {
            string source = """
                public static class MixerExtensions {
                    public static int Mix(this int value, int first, int second) {
                        return value + first + second;
                    }
                }

                public class Counter {
                    int GetValue() {
                        return 3;
                    }

                    int First() {
                        return 1;
                    }

                    int Second() {
                        return 2;
                    }

                    public int Read() {
                        return GetValue().Mix(First(), checked(Second() + 1));
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            int receiverIndex = output.IndexOf("this->GetValue()", StringComparison.Ordinal);
            int firstIndex = output.IndexOf("this->First()", StringComparison.Ordinal);
            int secondIndex = output.IndexOf("this->Second()", StringComparison.Ordinal);
            int helperIndex = output.IndexOf("Number::CheckedAdd(__checked_left_", StringComparison.Ordinal);
            Assert.True(receiverIndex >= 0 && receiverIndex < firstIndex);
            Assert.True(firstIndex < secondIndex);
            Assert.True(secondIndex < helperIndex);
        }

        /// <summary>
        /// Ensures invocation sequencing retains scoped temporary construction and cleanup injected by argument adaptation.
        /// </summary>
        [Fact]
        public void WriteOutput_WithScopedTemporaryBeforeCheckedArgument_RetainsScopedSetup() {
            string source = """
                using System.Collections.Generic;

                public class ShaderDefine {
                }

                public static class HlslShaderBindingParser {
                    static int Second() {
                        return 2;
                    }

                    static int ParseBindings(List<ShaderDefine> defines, int limit) {
                        return limit;
                    }

                    public static int Read() {
                        return ParseBindings(new List<ShaderDefine>(), checked(Second() + 1));
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            int scopedDeclarationIndex = output.IndexOf("auto __scopedArg", StringComparison.Ordinal);
            int scopedArgumentIndex = output.IndexOf("auto __invoke_arg", scopedDeclarationIndex + 1, StringComparison.Ordinal);
            int cleanupIndex = output.IndexOf("delete __scopedArg", StringComparison.Ordinal);
            Assert.True(scopedDeclarationIndex >= 0 && scopedDeclarationIndex < scopedArgumentIndex);
            Assert.True(cleanupIndex > scopedDeclarationIndex);
        }

        /// <summary>
        /// Ensures mixed-width checked addition remains an explicit diagnostic until operand promotion is emitted deliberately.
        /// </summary>
        [Fact]
        public void WriteOutput_WithMixedWidthCheckedAddition_ReportsCheckedArithmeticDiagnostic() {
            string source = """
                public class Counter {
                    public long Add(long left, int right) {
                        return checked(left + right);
                    }
                }
                """;

            RunConversion(source, out JsonDocument report);

            Assert.True(report.RootElement.GetProperty("hasErrors").GetBoolean());
            AssertDiagnostic(report, "AddExpression");
        }

        /// <summary>
        /// Ensures checked addition evaluates the left operand before the right operand and invokes the helper only after both values are captured.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSideEffectingCheckedAddition_CapturesOperandsLeftToRight() {
            string source = """
                public class Counter {
                    int ReadLeft() {
                        return 1;
                    }

                    int ReadRight() {
                        return 2;
                    }

                    public int Add() {
                        return checked(ReadLeft() + ReadRight());
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            int leftIndex = output.IndexOf("this->ReadLeft()", StringComparison.Ordinal);
            int rightIndex = output.IndexOf("this->ReadRight()", StringComparison.Ordinal);
            int helperIndex = output.IndexOf("Number::CheckedAdd(__checked_left_", StringComparison.Ordinal);
            Assert.True(leftIndex >= 0 && leftIndex < rightIndex);
            Assert.True(rightIndex < helperIndex);
        }

        /// <summary>
        /// Ensures checked compound assignment captures an indexed target before evaluating its right-hand value.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSideEffectingCheckedAddAssignment_CapturesTargetBeforeValue() {
            string source = """
                public class Counter {
                    int[] Values = new int[1];

                    int ReadIndex() {
                        return 0;
                    }

                    int ReadValue() {
                        return 1;
                    }

                    public void Add() {
                        checked {
                            Values[ReadIndex()] += ReadValue();
                        }
                    }
                }
                """;

            string output = RunConversion(source, out JsonDocument report);

            Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
            int targetIndex = output.IndexOf("this->ReadIndex()", StringComparison.Ordinal);
            int valueIndex = output.IndexOf("this->ReadValue()", StringComparison.Ordinal);
            int helperIndex = output.IndexOf("Number::CheckedAddAssign(__checked_target_", StringComparison.Ordinal);
            Assert.Contains("auto& __checked_target_", output);
            Assert.True(targetIndex >= 0 && targetIndex < valueIndex);
            Assert.True(valueIndex < helperIndex);
        }

        /// <summary>
        /// Ensures abstract properties are dispatched members rather than native storage accepted by checked by-reference helpers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCheckedAbstractPropertyMutation_ReportsCheckedMutationDiagnostic() {
            string source = """
                public abstract class Counter {
                    protected abstract int Value { get; set; }

                    public void Increment() {
                        checked {
                            Value++;
                        }
                    }
                }
                """;

            RunConversion(source, out JsonDocument report);

            Assert.True(report.RootElement.GetProperty("hasErrors").GetBoolean());
            AssertDiagnostic(report, "PostIncrementExpression");
        }

        /// <summary>
        /// Ensures user-defined indexers remain unsupported checked mutation targets instead of being passed as temporary getter values by reference.
        /// </summary>
        [Fact]
        public void WriteOutput_WithCheckedUserIndexerMutation_ReportsCheckedMutationDiagnostic() {
            string source = """
                public class IndexedCounter {
                    int Value;

                    public int this[int index] {
                        get {
                            return Value;
                        }
                        set {
                            Value = value;
                        }
                    }

                    public void Increment(int index) {
                        checked {
                            this[index]++;
                        }
                    }
                }
                """;

            RunConversion(source, out JsonDocument report);

            Assert.True(report.RootElement.GetProperty("hasErrors").GetBoolean());
            AssertDiagnostic(report, "PostIncrementExpression");
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns all generated textual output.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <param name="report">Parsed conversion report produced by the converter.</param>
        /// <returns>Concatenated generated file contents.</returns>
        static string RunConversion(string source, out JsonDocument report) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-unchecked-tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(rootPath, "Fixture.csproj");
            string sourcePath = Path.Combine(rootPath, "Fixture.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(rootPath);
            File.WriteAllText(projectPath, CreateProjectFile());
            File.WriteAllText(sourcePath, source);

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;

            CPPConversionRules rules = new CPPConversionRules();
            CPPCodeConverter converter = new CPPCodeConverter(rules, options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
            report = JsonDocument.Parse(File.ReadAllText(reportPath));
            return ReadGeneratedOutput(outputPath);
        }

        /// <summary>
        /// Creates a minimal SDK-style project file for temporary converter fixtures.
        /// </summary>
        /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
        static string CreateProjectFile() {
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
        /// Reads all generated headers and sources from a converter output directory into a single string for assertions.
        /// </summary>
        /// <param name="outputPath">Converter output directory to inspect.</param>
        /// <returns>Concatenated generated text content.</returns>
        static string ReadGeneratedOutput(string outputPath) {
            string[] files = Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return string.Join("\n", files.Select(File.ReadAllText));
        }

        /// <summary>
        /// Asserts that the conversion report contains no diagnostic entries for the supplied syntax kind.
        /// </summary>
        /// <param name="report">Parsed conversion report to inspect.</param>
        /// <param name="syntaxKind">Roslyn syntax kind that should be absent from the report.</param>
        static void AssertNoDiagnostic(JsonDocument report, string syntaxKind) {
            foreach (JsonElement diagnostic in report.RootElement.GetProperty("diagnostics").EnumerateArray()) {
                string actualSyntaxKind = diagnostic.GetProperty("syntaxKind").GetString() ?? string.Empty;
                Assert.NotEqual(syntaxKind, actualSyntaxKind);
            }
        }

        /// <summary>
        /// Asserts that the conversion report contains at least one error for the supplied checked arithmetic syntax.
        /// </summary>
        /// <param name="report">Parsed conversion report to inspect.</param>
        /// <param name="syntaxKind">Roslyn syntax kind that must be reported.</param>
        static void AssertDiagnostic(JsonDocument report, string syntaxKind) {
            foreach (JsonElement diagnostic in report.RootElement.GetProperty("diagnostics").EnumerateArray()) {
                string actualSyntaxKind = diagnostic.GetProperty("syntaxKind").GetString() ?? string.Empty;
                if (string.Equals(actualSyntaxKind, syntaxKind, StringComparison.Ordinal)) {
                    Assert.Equal("Error", diagnostic.GetProperty("severity").GetString());
                    return;
                }
            }

            Assert.Fail($"Expected a conversion diagnostic for checked arithmetic syntax '{syntaxKind}'.");
        }
    }
}
