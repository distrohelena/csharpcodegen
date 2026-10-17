using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Executes dictionary value enumeration and static application-directory behavior used by the Vita runtime profile.
/// </summary>
public sealed class CPPVitaRuntimeCompatibilityCompilationTests {
    /// <summary>
    /// Ensures generated foreach loops enumerate integer values and static-dot AppContext returns a managed string containing a dot.
    /// </summary>
    [Fact]
    public void DictionaryValuesAndStaticDotAppContext_ExecuteWithExpectedResults() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(DictionaryValuesAndStaticDotAppContext_ExecuteWithExpectedResults),
            """
            using System;
            using System.Collections.Generic;
            using cs2.attributes;

            public static class VitaRuntimeFixture {
                public static int Sum([NativeNoEscape] Dictionary<int, int> values) {
                    int sum = 0;
                    foreach (int value in values.Values) {
                        sum += value;
                    }
                    return sum;
                }

                public static int CountValues([NativeNoEscape] Dictionary<int, int> values) {
                    int count = 0;
                    foreach (int value in values.Values) {
                        count++;
                    }
                    return count;
                }

                public static string BaseDirectory() {
                    return AppContext.BaseDirectory;
                }
            }
            """);

        // The CLI maps its serialized mode into this enum before conversion; this fixture configures
        // the same option directly and regenerates through the real pipeline in the existing output directory.
        output.Converter.Options.AppContextBaseDirectoryMode = CPPAppContextBaseDirectoryMode.StaticDot;
        output.Converter.WriteOutput(output.OutputPath);
        string config = File.ReadAllText(Path.Combine(output.OutputPath, "helcpp_config.hpp"));
        Assert.Contains("#define HE_CPP_APP_CONTEXT_BASE_DIRECTORY_STATIC_DOT 1", config, StringComparison.Ordinal);

        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            static_assert(AppContext::BaseDirectory[0] == '.' && AppContext::BaseDirectory[1] == '\0');
            Dictionary<int32_t, int32_t> values;
            if (VitaRuntimeFixture::Sum(&values) != 0) return 1;
            if (VitaRuntimeFixture::CountValues(&values) != 0) return 2;
            values.Add(10, 5);
            values.Add(20, -3);
            values.Add(30, 9);
            if (VitaRuntimeFixture::Sum(&values) != 11) return 3;
            if (VitaRuntimeFixture::CountValues(&values) != 3) return 4;
            values.set_Item(20, 7);
            if (VitaRuntimeFixture::Sum(&values) != 21) return 5;
            if (VitaRuntimeFixture::CountValues(&values) != 3) return 6;
            if (VitaRuntimeFixture::BaseDirectory() != ".") return 7;
            """);

        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native exit code: {result.ProgramExitCode}. {result.ProgramOutput}");
    }
}
