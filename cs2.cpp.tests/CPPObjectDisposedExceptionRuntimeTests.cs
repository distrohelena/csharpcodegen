using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Compiles and executes native disposed-object construction used by batch rendering, retaining managed name/message diagnostics.</summary>
public sealed class CPPObjectDisposedExceptionRuntimeTests {
    /// <summary>Checks the two-string managed constructor, null/empty names and message fallback against the actual .NET exception.</summary>
    /// <param name="compactMessages">Whether exception text uses the compact runtime policy while object names remain available.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoArgumentConstructor_PreservesObjectNameAndMessage(bool compactMessages) {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "csharpcodegen");
        string fixtureRoot = Path.Combine(artifactRoot, "object-disposed-exception");
        string outputRoot = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            public static class DisposedFixture {
                public static void Fail(string objectName, string message) {
                    throw new System.ObjectDisposedException(objectName, message);
                }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PlatformOptionValues = new Dictionary<string, string> {
            [CPPCodegenOptionNames.UseExceptions] = "true",
            [CPPCodegenOptionNames.CompactNativeExceptionMessages] = compactMessages ? "true" : "false"
        };
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputRoot);
        Assert.False(converter.Report.HasErrors);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(outputRoot).Run("""
            #if defined(_WIN32)
            _setmode(_fileno(stdout), _O_BINARY);
            #endif
            const HeCppString objectName("Batch2DWriter");
            const HeCppString message("A disposed texture cannot be used by a batch run.");
            ObjectDisposedException unnamed;
            ObjectDisposedException oneArgument(objectName);
            ObjectDisposedException oneLiteral("Batch2DWriter");
            if (unnamed.get_ObjectName().is_null() || !unnamed.get_ObjectName().empty()) return 3;
            if (oneArgument.get_ObjectName() != objectName || oneLiteral.get_ObjectName() != objectName) return 4;
            #if !HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
            if (std::string(oneArgument.what()) != "Cannot access a disposed object: Batch2DWriter") return 5;
            #endif
            bool caught = false;
            try { DisposedFixture::Fail(objectName, message); }
            catch (InvalidOperationException* error) {
                auto* disposed = dynamic_cast<ObjectDisposedException*>(error);
                if (disposed == nullptr) { delete error; return 1; }
                std::cout << "named=" << disposed->get_ObjectName().c_str() << "|" << disposed->what() << "\n";
                delete error;
                caught = true;
            }
            if (!caught) return 2;
            ObjectDisposedException emptyName(HeCppString(""), message);
            std::cout << "empty=" << emptyName.get_ObjectName().c_str() << "|" << emptyName.what() << "\n";
            ObjectDisposedException nullName(HeCppString(nullptr), message);
            std::cout << "null-name=" << nullName.get_ObjectName().c_str() << "|" << nullName.what() << "\n";
            ObjectDisposedException nullMessage(objectName, HeCppString(nullptr));
            std::cout << "null-message=" << nullMessage.get_ObjectName().c_str() << "|" << nullMessage.what() << "\n";
            ObjectDisposedException literal("Batch2DWriter", "Literal disposed message.");
            std::cout << "literal=" << literal.get_ObjectName().c_str() << "|" << literal.what() << "\n";
            """, """
            #include "system/object_disposed_exception.hpp"
            #include <cstdio>
            #if defined(_WIN32)
            #include <io.h>
            #include <fcntl.h>
            #endif
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native exit {result.ProgramExitCode}: {result.ProgramOutput}");
        string[] labels = ["named", "empty", "null-name", "null-message", "literal"];
        ObjectDisposedException[] references = [
            new ObjectDisposedException("Batch2DWriter", "A disposed texture cannot be used by a batch run."),
            new ObjectDisposedException("", "A disposed texture cannot be used by a batch run."),
            new ObjectDisposedException(null, "A disposed texture cannot be used by a batch run."),
            new ObjectDisposedException("Batch2DWriter", (string)null!),
            new ObjectDisposedException("Batch2DWriter", "Literal disposed message.")
        ];
        string expected = string.Join("\n", references.Select((reference, index) =>
            labels[index] + "=" + reference.ObjectName + "|" +
            (compactMessages ? "Cannot access a disposed object." : reference.Message)));
        Assert.Equal(expected.Replace("\r\n", "\n"), result.ProgramOutput.Replace("\r\n", "\n").Trim());
    }
}
