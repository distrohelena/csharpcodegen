using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>Verifies that separately converted projects use the string accessor contract when the constant owner is available only as managed assembly metadata.</summary>
public sealed class CPPExternalStringConstantTests {
    /// <summary>Reproduces authored scene code consuming an engine constant from another conversion pass, including source getter-name collisions.</summary>
    [Fact]
    public void WriteOutput_MetadataConstantOwner_UsesMatchingAccessors() {
        string root = Path.Combine(Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetTempPath(), "external-string-constant");
        Directory.CreateDirectory(root);
        string source = """
            public class ExternalConstants {
                public const string Text = "external";
                public const string Empty = "";
                static int get_Text() { return 17; }
            }
            """;
        string assemblyPath = Path.Combine(root, "External.dll");
        CSharpCompilation compilation = CSharpCompilation.Create("External", [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Microsoft.CodeAnalysis.Emit.EmitResult assembly = compilation.Emit(assemblyPath);
        Assert.True(assembly.Success, string.Join(Environment.NewLine, assembly.Diagnostics));
        File.WriteAllText(Path.Combine(root, "Consumer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
              <ItemGroup><Reference Include="External"><HintPath>External.dll</HintPath></Reference></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Consumer.cs"), """
            public class Consumer {
                public string Read() { return ExternalConstants.Text; }
                public string ReadEmpty() { return ExternalConstants.Empty; }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(root, "Consumer.csproj"));
        string output = Path.Combine(root, "generated");
        converter.WriteOutput(output);
        Assert.False(converter.Report.HasErrors);
        string consumer = File.ReadAllText(Path.Combine(output, "Consumer.cpp"));
        Assert.Contains("ExternalConstants::Text()", consumer);
        Assert.Contains("ExternalConstants::Empty()", consumer);
        Assert.DoesNotContain("ExternalConstants::Text;", consumer);
    }
}
