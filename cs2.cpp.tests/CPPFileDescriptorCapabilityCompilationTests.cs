using cs2.cpp;
using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Executes generated file streams to verify limited-libc read support and mutation rejection without filesystem side effects.
/// </summary>
public sealed class CPPFileDescriptorCapabilityCompilationTests {
    /// <summary>
    /// Ensures the stdio fallback does not enable path access when the hosted filesystem is disabled.
    /// </summary>
    [Fact]
    public void FileStreamRead_WithoutHostedFileSystem_RemainsUnavailable() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(FileStreamRead_WithoutHostedFileSystem_RemainsUnavailable),
            """
            using System.IO;
            using cs2.attributes;

            public static class FileDescriptorFixture {
                [NativeOwnedReturn]
                public static FileStream OpenRead(string path) {
                    return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
            }
            """,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["native-file-stream-descriptor-apis"] = "false",
                [CPPCodegenOptionNames.UseHostedFileSystem] = "false",
                [CPPCodegenOptionNames.UseExceptions] = "true"
            });

        string sourcePath = Path.Combine(output.WorkspacePath, "unavailable.bin");
        File.WriteAllBytes(sourcePath, new byte[] { 7, 8, 9 });
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run($$"""
            bool rejected = false;
            try { delete FileDescriptorFixture::OpenRead(R"fixture({{sourcePath}})fixture"); }
            catch (const NotSupportedException&) { rejected = true; }
            if (!rejected) return 1;
            """);

        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native exit code: {result.ProgramExitCode}. {result.ProgramOutput}");
        Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(sourcePath));
    }

    /// <summary>
    /// Reads and seeks an actual file through both constructor overloads, without taking an in-memory snapshot,
    /// and verifies read-only streams reject mutation before affecting existing or missing paths.
    /// </summary>
    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public void FileStreamRead_UsesLiveReadOnlyFile_WithAndWithoutDescriptorApis(string descriptorApis) {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(FileStreamRead_UsesLiveReadOnlyFile_WithAndWithoutDescriptorApis),
            """
            using System.IO;
            using cs2.attributes;

            public static class FileDescriptorFixture {
                [NativeOwnedReturn]
                public static FileStream Open(string path) {
                    return new FileStream(path, FileMode.Open);
                }

                [NativeOwnedReturn]
                public static FileStream OpenRead(string path) {
                    return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
            }
            """,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["native-file-stream-descriptor-apis"] = descriptorApis,
                [CPPCodegenOptionNames.UseExceptions] = "true"
            });

        string sourcePath = Path.Combine(output.WorkspacePath, "read-only.bin");
        string missingPath = Path.Combine(output.WorkspacePath, "must-remain-missing.bin");
        byte[] expectedBytes = { 11, 22, 33, 44, 55 };
        File.WriteAllBytes(sourcePath, expectedBytes);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run($$"""
            const char* path = R"fixture({{sourcePath}})fixture";
            const char* missing = R"fixture({{missingPath}})fixture";
            try {
                FileStream* stream = FileDescriptorFixture::Open(path);
                if (!stream->CanRead() || !stream->CanSeek() || stream->CanWrite()) return 10;
                if (stream->Length() != 5 || stream->Position() != 0) return 11;

                // A change after opening must be observed: path-backed reads must not copy the whole file.
                std::FILE* writer = std::fopen(path, "r+b");
                if (!writer) return 12;
                if (std::fseek(writer, 4, SEEK_SET) != 0 || std::fputc(99, writer) == EOF) return 13;
                if (std::fclose(writer) != 0) return 14;
                uint8_t bytes[8] = {};
                if (stream->Seek(-1, SeekOrigin::End) != 4 || stream->Read(bytes, 0, 1) != 1 || bytes[0] != 99) return 15;
                if (stream->Read(bytes, 0, 1) != 0) return 16;
                stream->Seek(0, SeekOrigin::Begin);
                if (stream->Read(bytes, 1, 2) != 2 || bytes[0] != 99 || bytes[1] != 11 || bytes[2] != 22) return 17;
                if (stream->Seek(1, SeekOrigin::Current) != 3 || stream->Position() != 3) return 18;
                if (stream->Read(bytes, 0, 8) != 2 || bytes[0] != 44 || bytes[1] != 99) return 19;
                if (stream->Read(bytes, 0, 1) != 0) return 20;
                stream->SetPosition(2);
                if (stream->Read(bytes, 0, 1) != 1 || bytes[0] != 33) return 21;

                bool rejected = false;
                try { stream->Write(bytes, 0, 1); } catch (const NotSupportedException&) { rejected = true; }
                if (!rejected) return 22;
                rejected = false;
                try { stream->SetLength(0); } catch (const NotSupportedException&) { rejected = true; }
                if (!rejected) return 23;
                stream->Dispose();
                stream->Dispose();
                if (stream->CanRead() || stream->CanSeek() || stream->CanWrite()) return 24;
                delete stream;

                FileStream* explicitRead = FileDescriptorFixture::OpenRead(path);
                if (explicitRead->Length() != 5 || explicitRead->CanWrite() || explicitRead->Read(bytes, 0, 1) != 1 || bytes[0] != 11) return 25;
                delete explicitRead;
                rejected = false;
                try { delete FileDescriptorFixture::OpenRead(missing); } catch (const FileNotFoundException&) { rejected = true; }
                if (!rejected) return 26;

            #if !HE_CPP_RUNTIME_HAS_FILE_DESCRIPTOR_APIS
                const FileMode modes[] = {FileMode::Append, FileMode::Create, FileMode::CreateNew, FileMode::OpenOrCreate, FileMode::Truncate};
                for (const char* candidate : {path, missing}) {
                    for (FileMode mode : modes) {
                        rejected = false;
                        try { FileStream invalid(candidate, mode); } catch (const NotSupportedException&) { rejected = true; }
                        if (!rejected) return 27;
                    }
                    for (FileAccess access : {FileAccess::Write, FileAccess::ReadWrite}) {
                        rejected = false;
                        try { FileStream invalid(candidate, FileMode::Open, access, FileShare::ReadWrite); }
                        catch (const NotSupportedException&) { rejected = true; }
                        if (!rejected) return 28;
                    }
                    rejected = false;
                    try { FileStream invalid(candidate, FileMode::OpenOrCreate, FileAccess::Read, FileShare::ReadWrite); }
                    catch (const NotSupportedException&) { rejected = true; }
                    if (!rejected) return 29;
                }
            #endif
                // Restore the byte changed by the test's independent stdio writer.
                writer = std::fopen(path, "r+b");
                if (!writer || std::fseek(writer, 4, SEEK_SET) != 0 || std::fputc(55, writer) == EOF || std::fclose(writer) != 0) return 30;
            } catch (const std::exception& error) {
                std::cerr << error.what();
                return 31;
            } catch (...) {
                return 32;
            }
            """);

        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native exit code: {result.ProgramExitCode}. {result.ProgramOutput}");
        Assert.Equal(expectedBytes, File.ReadAllBytes(sourcePath));
        Assert.False(File.Exists(missingPath), "Rejected operations must not create a missing file.");
    }

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
