using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Compiles file-stream creation with descriptor APIs disabled and verifies rejection has no filesystem side effects.
/// </summary>
public sealed class CPPFileDescriptorCapabilityCompilationTests {
    /// <summary>
    /// Ensures FileMode.Create raises the runtime's NotSupportedException before opening or creating the destination file.
    /// </summary>
    [Fact]
    public void FileStreamCreate_WithoutDescriptorApis_ThrowsBeforeCreatingDestination() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(FileStreamCreate_WithoutDescriptorApis_ThrowsBeforeCreatingDestination),
            """
            using System.IO;
            using cs2.attributes;

            public static class FileDescriptorFixture {
                [NativeOwnedReturn]
                public static FileStream Create(string path) {
                    return new FileStream(path, FileMode.Create);
                }
            }
            """,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["native-file-stream-descriptor-apis"] = "false",
                [CPPCodegenOptionNames.UseExceptions] = "true"
            });

        string destinationPath = Path.Combine(output.WorkspacePath, "must-not-be-created.bin");
        Assert.False(File.Exists(destinationPath));
        string escapedDestinationPath = destinationPath.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        string config = File.ReadAllText(Path.Combine(output.OutputPath, "helcpp_config.hpp"));
        Assert.Contains("#define HE_CPP_RUNTIME_HAS_FILE_DESCRIPTOR_APIS 0", config, StringComparison.Ordinal);

        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run($$"""
            bool rejected = false;
            try {
                FileStream* stream = FileDescriptorFixture::Create("{{escapedDestinationPath}}");
                delete stream;
            } catch (const NotSupportedException&) {
                rejected = true;
            } catch (...) {
                return 2;
            }
            if (!rejected) return 1;
            """);

        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native exit code: {result.ProgramExitCode}. {result.ProgramOutput}");
        Assert.False(File.Exists(destinationPath), "FileMode.Create must reject unsupported descriptor APIs before creating the destination.");
    }
}
