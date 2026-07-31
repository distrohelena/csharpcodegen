using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies representative semantic ownership transitions produce C++ accepted by the native compile harness.
/// </summary>
public sealed class CPPOwnershipGeneratedCompilationTests {
    /// <summary>
    /// Ensures direct cleanup, factory cleanup, return and parameter transfers, explicit release, replacement, and branch cleanup compile together.
    /// </summary>
    [Fact]
    public void WriteOutput_WithRepresentativeOwnershipTransitions_CompilesGeneratedCpp() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(WriteOutput_WithRepresentativeOwnershipTransitions_CompilesGeneratedCpp),
            """
            using System.Collections.Generic;
            using cs2.attributes;

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }

                public static void Release<T>(ref T value) where T : class {
                    value = null;
                }
            }

            public sealed class OwnershipCases {
                public void DirectCleanup() {
                    List<int> direct = new List<int>();
                    direct.Add(1);
                }

                public int OwnedFactoryCleanup() {
                    List<int> factory = Build();
                    return factory.Count;
                }

                public List<int> ReturnTransfer() {
                    List<int> returned = new List<int>();
                    return returned;
                }

                public void ParameterTransfer() {
                    List<int> transferred = new List<int>();
                    Take(transferred);
                }

                public void ExplicitRelease() {
                    List<int> released = new List<int>();
                    NativeOwnership.Release(ref released);
                }

                public void Reassignment() {
                    List<int> replaced = new List<int>();
                    replaced = new List<int>();
                }

                public int BranchCleanup(bool exitEarly) {
                    List<int> branch = new List<int>();
                    if (exitEarly) {
                        return 1;
                    }

                    return branch.Count;
                }

                static List<int> Build() {
                    List<int> built = new List<int>();
                    return built;
                }

                static void Take([NativeTakesOwnership] List<int> values) {
                    NativeOwnership.Delete(values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(output.OutputPath, "OwnershipCases.cpp"));

        Assert.Equal(1, CountOccurrences(sourceOutput, "delete direct;"));
        Assert.Equal(1, CountOccurrences(sourceOutput, "delete factory;"));
        Assert.Equal(1, CountOccurrences(sourceOutput, "delete returned;"));
        Assert.Equal(1, CountOccurrences(sourceOutput, "delete transferred;"));
        Assert.Equal(2, CountOccurrences(sourceOutput, "delete released;"));
        Assert.Equal(2, CountOccurrences(sourceOutput, "delete replaced;"));
        Assert.Equal(1, CountOccurrences(sourceOutput, "delete branch;"));

        int exitCode = output.CompileGeneratedOutput(out string compilerOutput);
        Assert.True(exitCode == 0, compilerOutput);
    }

    /// <summary>
    /// Counts non-overlapping occurrences of one fragment in generated source.
    /// </summary>
    /// <param name="text">Generated source text to inspect.</param>
    /// <param name="fragment">Non-empty fragment whose occurrences are counted.</param>
    /// <returns>The number of non-overlapping fragment occurrences.</returns>
    static int CountOccurrences(string text, string fragment) {
        if (string.IsNullOrEmpty(fragment)) {
            throw new ArgumentException("A generated-source fragment is required.", nameof(fragment));
        }

        int count = 0;
        int searchStart = 0;
        while (searchStart < text.Length) {
            int fragmentIndex = text.IndexOf(fragment, searchStart, StringComparison.Ordinal);
            if (fragmentIndex < 0) {
                break;
            }

            count++;
            searchStart = fragmentIndex + fragment.Length;
        }

        return count;
    }
}
