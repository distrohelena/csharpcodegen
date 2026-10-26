using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Exercises native character-editor boundaries that ordinary managed tests cannot validate.</summary>
public sealed class CPPNotepadRuntimeCompatibilityTests {
    /// <summary>Compiles Windows macro-sensitive names, typed data failures and managed-array file I/O together.</summary>
    [Fact]
    public void GeneratedRuntime_PreservesNamesExceptionsAndFileBuffers() {
        CPPCompileValidationRegressionTests.ConversionOutput output = CPPCompileValidationRegressionTests.RunConversion("""
            public static class RuntimeFixture {
                public static bool IsNear(int distance) {
                    bool near = distance < 4;
                    return near;
                }
                public static void Fail() { throw new System.IO.InvalidDataException("Invalid notepad frame."); }
            }
            """, useExceptions: true);
        Assert.Contains("near_", output.GeneratedText, StringComparison.Ordinal);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            if (!RuntimeFixture::IsNear(2) || RuntimeFixture::IsNear(8)) return 1;
            bool caught = false;
            try { RuntimeFixture::Fail(); }
            catch (InvalidDataException* error) { delete error; caught = true; }
            if (!caught) return 2;
            FileStream stream("managed-buffer.bin", FileMode::Create, FileAccess::ReadWrite, FileShare::ReadWrite);
            Array<uint8_t> input(3);
            input.Data[0] = 65; input.Data[1] = 0; input.Data[2] = 233;
            stream.Write(&input, 0, 3);
            stream.Seek(0, SeekOrigin::Begin);
            Array<uint8_t> actual(3);
            if (stream.Read(&actual, 0, 3) != 3 || actual.Data[0] != 65 || actual.Data[1] != 0 || actual.Data[2] != 233) return 3;
            stream.Dispose();
            return 0;
            """, """
            #if defined(_WIN32)
            #ifndef NOMINMAX
            #define NOMINMAX
            #endif
            #include <windows.h>
            #endif
            #include "system/io/file-stream.hpp"
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
    }
}