using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that emitted source files include only the runtime helpers used by the current generated type.
/// </summary>
public class CPPClassEmitterRuntimeRequirementScopeTests {
    /// <summary>
    /// Ensures runtime helper includes are scoped to the current type instead of the full conversion run.
    /// </summary>
    [Fact]
    public void WriteOutput_WhenDifferentTypesUseDifferentHelpers_SourceIncludesOnlyCurrentTypeRequirements() {
        string source = """
using System.Text;
using System.IO;

namespace Fixture {
    public class BuilderUser {
        public string Build() {
            StringBuilder builder = new StringBuilder();
            builder.Append("x");
            return builder.ToString();
        }
    }

    public class ReaderUser {
        public string Read(string source) {
            using StringReader reader = new StringReader(source);
            return reader.ReadLine();
        }
    }
}
""";

        string outputPath = RunConversion(source);
        string builderSource = File.ReadAllText(Path.Combine(outputPath, "BuilderUser.cpp"));
        string readerSource = File.ReadAllText(Path.Combine(outputPath, "ReaderUser.cpp"));

        Assert.Contains("#include \"system/text/string-builder.hpp\"", builderSource);
        Assert.DoesNotContain("#include \"system/io/string-reader.hpp\"", builderSource);

        Assert.Contains("#include \"system/io/string-reader.hpp\"", readerSource);
        Assert.DoesNotContain("#include \"system/text/string-builder.hpp\"", readerSource);
    }

    /// <summary>
    /// Runs the converter against a temporary single-file project and returns the generated output folder.
    /// </summary>
    /// <param name="source">C# source file content to convert.</param>
    /// <returns>The generated output directory.</returns>
    static string RunConversion(string source) {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-runtime-scope-tests", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(rootPath, "Fixture.csproj");
        string sourcePath = Path.Combine(rootPath, "Fixture.cs");
        string outputPath = Path.Combine(rootPath, "out");

        Directory.CreateDirectory(rootPath);
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(sourcePath, source);

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);
        return outputPath;
    }
}
