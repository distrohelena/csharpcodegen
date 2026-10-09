using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies external component dependencies remain declared when project code is converted separately from its referenced assemblies.
/// </summary>
public sealed class CPPExternalProjectHeaderDependencyTests {
    /// <summary>
    /// Converts a complete project with two source-project or metadata references and checks inherited and parameter types in its standalone header.
    /// </summary>
    /// <param name="useMetadataReferences">Selects already-built assembly references, matching separate engine and project conversion.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOutput_WithTwoExternalComponentReferences_DeclaresBaseAndSignatureDependencies(bool useMetadataReferences) {
        string fixtureRoot = Path.Combine("C:/dev/helworks/builds/helengine-n64/menu-20261007/indexed-textures", "generator-fixtures", Guid.NewGuid().ToString("N"));
        string coreDirectory = Path.Combine(fixtureRoot, "Core");
        string scrollingDirectory = Path.Combine(fixtureRoot, "Scrolling");
        string menuDirectory = Path.Combine(fixtureRoot, "Menu");
        Directory.CreateDirectory(coreDirectory);
        Directory.CreateDirectory(scrollingDirectory);
        Directory.CreateDirectory(menuDirectory);
        string coreSource = "namespace Engine { public class UpdateComponent { public virtual void Update() {} } }";
        string scrollingSource = "namespace Engine { public class ScrollComponent { public int Offset; } }";
        File.WriteAllText(Path.Combine(coreDirectory, "UpdateComponent.cs"), coreSource);
        File.WriteAllText(Path.Combine(scrollingDirectory, "ScrollComponent.cs"), scrollingSource);
        File.WriteAllText(Path.Combine(coreDirectory, "Core.csproj"), CreateProject(""));
        File.WriteAllText(Path.Combine(scrollingDirectory, "Scrolling.csproj"), CreateProject(""));
        string references;
        if (useMetadataReferences) {
            WriteReferenceAssembly(Path.Combine(coreDirectory, "Core.dll"), "Core", coreSource);
            WriteReferenceAssembly(Path.Combine(scrollingDirectory, "Scrolling.dll"), "Scrolling", scrollingSource);
            references = """
                <Reference Include="Core"><HintPath>../Core/Core.dll</HintPath></Reference>
                <Reference Include="Scrolling"><HintPath>../Scrolling/Scrolling.dll</HintPath></Reference>
                """;
        } else {
            references = """
                <ProjectReference Include="../Core/Core.csproj" />
                <ProjectReference Include="../Scrolling/Scrolling.csproj" />
                """;
        }
        string projectPath = Path.Combine(menuDirectory, "Menu.csproj");
        File.WriteAllText(projectPath, CreateProject(references));
        File.WriteAllText(Path.Combine(menuDirectory, "MenuComponent.cs"), """
            using Engine;
            public class MenuComponent : UpdateComponent {
                public ScrollComponent ResolveScroll(ScrollComponent scroll) { return scroll; }
                public override void Update() {}
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        string outputPath = Path.Combine(fixtureRoot, "generated");
        converter.WriteOutput(outputPath);
        string header = File.ReadAllText(Path.Combine(outputPath, "MenuComponent.hpp"));
        Assert.Contains("#include \"UpdateComponent.hpp\"", header, StringComparison.Ordinal);
        Assert.True(header.Contains("#include \"ScrollComponent.hpp\"", StringComparison.Ordinal) ||
            header.Contains("class ScrollComponent;", StringComparison.Ordinal), header);
        Assert.Contains("public ::UpdateComponent", header, StringComparison.Ordinal);
        Assert.DoesNotContain("#include \"?.hpp\"", header, StringComparison.Ordinal);
    }

    /// <summary>Creates a standalone SDK-style project with the supplied external references.</summary>
    /// <param name="references">Reference item declarations inserted into the project.</param>
    /// <returns>The project content used by the real Roslyn project loader.</returns>
    static string CreateProject(string references) {
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
              <ItemGroup>{{references}}</ItemGroup>
            </Project>
            """;
    }

    /// <summary>Builds the fixture's external assembly without depending on project-reference source preprocessing.</summary>
    /// <param name="assemblyPath">Visible workspace-owned output assembly path.</param>
    /// <param name="assemblyName">Identity of the emitted fixture assembly.</param>
    /// <param name="source">Component source compiled into the external assembly.</param>
    static void WriteReferenceAssembly(string assemblyPath, string assemblyName, string source) {
        CSharpCompilation compilation = CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(assemblyPath);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
