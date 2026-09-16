using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Compiles and executes generated C++ for managed string splitting and math helpers, comparing the native results
/// against the real .NET behavior of the same calls instead of only inspecting emitted text.
/// </summary>
public sealed class CPPNativeRuntimeExecutionTests {
    /// <summary>
    /// Ensures every supported character-based Split form and the Math sign/inverse-sine helpers execute with .NET parity.
    /// </summary>
    [Fact]
    public void GeneratedProgram_WithStringSplitAndMathCalls_MatchesDotNetBehavior() {
        // Math.Sign(NaN) reports through the configured failure policy: it throws only when the
        // runtime is generated with C++ exception unwinding, which this program catches.
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(GeneratedProgram_WithStringSplitAndMathCalls_MatchesDotNetBehavior),
            """
            using System;

            public static class SplitFixture {
                public static string[] ByChar(string value) {
                    return value.Split('\n');
                }

                public static string[] ByCharNone(string value) {
                    return value.Split('\n', StringSplitOptions.None);
                }

                public static string[] ByCharRemoveEmpty(string value) {
                    return value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                }

                public static string[] ByCharCount(string value, int count) {
                    return value.Split('\n', count);
                }

                public static string[] ByCharCountRemoveEmpty(string value, int count) {
                    return value.Split('\n', count, StringSplitOptions.RemoveEmptyEntries);
                }

                public static string[] BySet(string value) {
                    return value.Split(new[] { ',', ';' });
                }

                public static string[] BySetCountNone(string value, int count) {
                    return value.Split(new[] { ',', ';' }, count, StringSplitOptions.None);
                }
            }

            public static class MathFixture {
                public static int SignDouble(double value) {
                    return Math.Sign(value);
                }

                public static int SignFloat(float value) {
                    return Math.Sign(value);
                }

                public static int SignInt(int value) {
                    return Math.Sign(value);
                }

                public static double Asin(double value) {
                    return Math.Asin(value);
                }
            }
            """,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                [CPPCodegenOptionNames.UseExceptions] = "true"
            });

        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run(
            """
            auto escape = [](const std::string& text) {
                std::string escaped;
                for (char character : text) {
                    if (character == '\n') {
                        escaped += "\\n";
                    } else {
                        escaped += character;
                    }
                }
                return escaped;
            };
            auto emitParts = [&](const char* label, Array<std::string>* parts) {
                std::cout << label << "=";
                for (int32_t index = 0; index < parts->Length; index++) {
                    if (index > 0) {
                        std::cout << "|";
                    }
                    std::cout << "[" << escape((*parts)[index]) << "]";
                }
                std::cout << "\n";
                delete parts;
            };
            emitParts("ByChar", SplitFixture::ByChar("a\n\nb\n"));
            emitParts("ByCharNone", SplitFixture::ByCharNone("a\n\nb\n"));
            emitParts("ByCharRemoveEmpty", SplitFixture::ByCharRemoveEmpty("a\n\nb\n"));
            emitParts("ByCharCount2", SplitFixture::ByCharCount("a\nb\nc", 2));
            emitParts("ByCharCount1", SplitFixture::ByCharCount("a\nb", 1));
            emitParts("ByCharCount0", SplitFixture::ByCharCount("a\nb", 0));
            emitParts("ByCharCountRemoveEmpty2", SplitFixture::ByCharCountRemoveEmpty("a\n\nb\nc", 2));
            emitParts("ByCharCountRemoveEmptyTrailing", SplitFixture::ByCharCountRemoveEmpty("a\n\n", 2));
            emitParts("BySet", SplitFixture::BySet("a,b;c;;d"));
            emitParts("BySetCountNone2", SplitFixture::BySetCountNone("a,b;c", 2));
            try {
                emitParts("NegativeCount", SplitFixture::ByCharCount("a\nb", -1));
            } catch (const ArgumentOutOfRangeException&) {
                std::cout << "NegativeCount=ArgumentOutOfRangeException\n";
            }
            std::cout << "SignDoubleNegative=" << MathFixture::SignDouble(-2.5) << "\n";
            std::cout << "SignDoubleZero=" << MathFixture::SignDouble(0.0) << "\n";
            std::cout << "SignDoublePositive=" << MathFixture::SignDouble(3.25) << "\n";
            std::cout << "SignFloatNegative=" << MathFixture::SignFloat(-0.5f) << "\n";
            std::cout << "SignIntNegative=" << MathFixture::SignInt(-7) << "\n";
            std::cout << "SignIntZero=" << MathFixture::SignInt(0) << "\n";
            std::cout << "SignIntPositive=" << MathFixture::SignInt(9) << "\n";
            try {
                int32_t nanSign = MathFixture::SignDouble(std::nan(""));
                std::cout << "SignNaN=" << nanSign << "\n";
            } catch (const ArithmeticException&) {
                std::cout << "SignNaN=ArithmeticException\n";
            }
            std::cout.setf(std::ios::fixed);
            std::cout.precision(6);
            std::cout << "AsinOne=" << MathFixture::Asin(1.0) << "\n";
            std::cout << "AsinNegativeHalf=" << MathFixture::Asin(-0.5) << "\n";
            std::cout << "AsinZero=" << MathFixture::Asin(0.0) << "\n";
            """);

        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);

        string[] expectedLines = {
            FormatParts("ByChar", "a\n\nb\n".Split('\n')),
            FormatParts("ByCharNone", "a\n\nb\n".Split('\n', StringSplitOptions.None)),
            FormatParts("ByCharRemoveEmpty", "a\n\nb\n".Split('\n', StringSplitOptions.RemoveEmptyEntries)),
            FormatParts("ByCharCount2", "a\nb\nc".Split('\n', 2)),
            FormatParts("ByCharCount1", "a\nb".Split('\n', 1)),
            FormatParts("ByCharCount0", "a\nb".Split('\n', 0)),
            FormatParts("ByCharCountRemoveEmpty2", "a\n\nb\nc".Split('\n', 2, StringSplitOptions.RemoveEmptyEntries)),
            FormatParts("ByCharCountRemoveEmptyTrailing", "a\n\n".Split('\n', 2, StringSplitOptions.RemoveEmptyEntries)),
            FormatParts("BySet", "a,b;c;;d".Split(new[] { ',', ';' })),
            FormatParts("BySetCountNone2", "a,b;c".Split(new[] { ',', ';' }, 2, StringSplitOptions.None)),
            "NegativeCount=" + Assert.Throws<ArgumentOutOfRangeException>(() => "a\nb".Split('\n', -1)).GetType().Name,
            "SignDoubleNegative=" + Math.Sign(-2.5),
            "SignDoubleZero=" + Math.Sign(0.0),
            "SignDoublePositive=" + Math.Sign(3.25),
            "SignFloatNegative=" + Math.Sign(-0.5f),
            "SignIntNegative=" + Math.Sign(-7),
            "SignIntZero=" + Math.Sign(0),
            "SignIntPositive=" + Math.Sign(9),
            "SignNaN=" + Assert.Throws<ArithmeticException>(() => Math.Sign(double.NaN)).GetType().Name,
            "AsinOne=" + Math.Asin(1.0).ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
            "AsinNegativeHalf=" + Math.Asin(-0.5).ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
            "AsinZero=" + Math.Asin(0.0).ToString("F6", System.Globalization.CultureInfo.InvariantCulture)
        };

        Assert.Equal(expectedLines, result.ProgramOutputLines);
    }

    /// <summary>
    /// Formats one split result the same way the native program prints it, with newline characters escaped.
    /// </summary>
    /// <param name="label">Case label printed before the parts.</param>
    /// <param name="parts">Split result produced by .NET.</param>
    /// <returns>The canonical single-line representation.</returns>
    static string FormatParts(string label, string[] parts) {
        return label + "=" + string.Join("|", parts.Select(part => "[" + part.Replace("\n", "\\n") + "]"));
    }
}
