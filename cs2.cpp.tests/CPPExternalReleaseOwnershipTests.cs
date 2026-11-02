using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>Protects exception cleanup when a consuming release method is visible only through assembly metadata.</summary>
public sealed class CPPExternalReleaseOwnershipTests {
    /// <summary>Transfers a factory-owned local only on paths that enter the external consuming release call.</summary>
    /// <param name="declareOwnership">Whether the separately compiled release method publishes its consuming contract.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOutput_MetadataReleaseInCatch_TransfersGuardAtCallBoundary(bool declareOwnership) {
        string root = Path.Combine(Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetTempPath(), "external-release-ownership", declareOwnership ? "owned" : "unspecified");
        Directory.CreateDirectory(root);
        string contract = declareOwnership ? "[NativeTakesOwnership]" : string.Empty;
        string producer = $$"""
            using System;
            namespace helengine {
                [AttributeUsage(AttributeTargets.Parameter)]
                public sealed class NativeTakesOwnershipAttribute : Attribute { }
                public class RuntimeMaterial { }
                public abstract class RenderManager3D {
                    public virtual void ReleaseMaterial({{contract}} RuntimeMaterial material) { }
                    public virtual void ApplyTextures(bool fail, bool releaseFails) { }
                }
            }
            """;
        string assemblyPath = Path.Combine(root, "External.dll");
        CSharpCompilation compilation = CSharpCompilation.Create("External",
            [CSharpSyntaxTree.ParseText(producer)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Microsoft.CodeAnalysis.Emit.EmitResult assembly = compilation.Emit(assemblyPath);
        Assert.True(assembly.Success, string.Join(Environment.NewLine, assembly.Diagnostics));
        // Match the producer's runtime core library so the SDK consumer binds without reference/runtime type mismatches.
        File.WriteAllText(Path.Combine(root, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>disable</Nullable><DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences></PropertyGroup>
              <ItemGroup><Reference Include="External"><HintPath>External.dll</HintPath></Reference><Reference Include="System.Private.CoreLib"><HintPath>{{System.Security.SecurityElement.Escape(typeof(object).Assembly.Location)}}</HintPath></Reference></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Consumer.cs"), """
            using helengine;
            public class MaterialLoader {
                public static RuntimeMaterial Build() { return new RuntimeMaterial(); }
                public static RuntimeMaterial Load(RenderManager3D renderer, bool fail, bool release, bool earlyThrow, bool releaseFails) {
                    RuntimeMaterial material = Build();
                    try {
                        renderer.ApplyTextures(fail, releaseFails);
                        return material;
                    } catch {
                        if (earlyThrow) { throw; }
                        if (release) { renderer.ReleaseMaterial(material); }
                        throw;
                    }
                }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.PlatformOptionValues = new Dictionary<string, string> {
            [CPPCodegenOptionNames.UseExceptions] = "true"
        };
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.RequireCleanCompilation = true;
        converter.AddCsproj(Path.Combine(root, "Consumer.csproj"));
        string output = Path.Combine(root, "generated");
        converter.WriteOutput(output);
        Assert.False(converter.Report.HasErrors);
        string source = File.ReadAllText(Path.Combine(output, "MaterialLoader.cpp"));
        Assert.Contains("he_cpp_make_scope_exit", source);
        System.Text.RegularExpressions.Match flag = System.Text.RegularExpressions.Regex.Match(source, @"bool (__owns_material_[A-Fa-f0-9]+) = true;");
        Assert.True(flag.Success, source);
        int catchStart = source.IndexOf("catch (...)", StringComparison.Ordinal);
        Assert.True(catchStart >= 0, source);
        int releaseCall = source.IndexOf("renderer->ReleaseMaterial(material);", catchStart, StringComparison.Ordinal);
        Assert.True(releaseCall > catchStart, source);
        string cleanup = source.Substring(catchStart, releaseCall - catchStart);
        Assert.Equal(declareOwnership, cleanup.Contains(flag.Groups[1].Value + " = false;", StringComparison.Ordinal));
        if (!declareOwnership) {
            return;
        }

        File.WriteAllText(Path.Combine(output, "RuntimeMaterial.hpp"), """
            #pragma once
            class RuntimeMaterial {
            public:
                inline static int Destroyed = 0;
                virtual ~RuntimeMaterial() { ++Destroyed; }
            };
            """);
        File.WriteAllText(Path.Combine(output, "RenderManager3D.hpp"), """
            #pragma once
            #include "RuntimeMaterial.hpp"
            #include <stdexcept>
            class RenderManager3D {
                bool ReleaseFails = false;
            public:
                int Releases = 0;
                void ApplyTextures(bool fail, bool releaseFails) {
                    ReleaseFails = releaseFails;
                    if (fail) throw std::runtime_error("textures");
                }
                void ReleaseMaterial(RuntimeMaterial* material) {
                    ++Releases;
                    delete material;
                    if (ReleaseFails) throw std::runtime_error("release");
                }
            };
            """);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output).Run("""
            RenderManager3D renderer;
            auto material = MaterialLoader::Load(&renderer, false, true, false, false);
            if (RuntimeMaterial::Destroyed != 0 || renderer.Releases != 0) return 1;
            delete material;
            for (bool release : {false, true}) {
                for (bool earlyThrow : {false, true}) {
                    for (bool releaseFails : {false, true}) {
                        int destroyed = RuntimeMaterial::Destroyed;
                        int releases = renderer.Releases;
                        bool caught = false;
                        try { MaterialLoader::Load(&renderer, true, release, earlyThrow, releaseFails); }
                        catch (...) { caught = true; }
                        int expectedReleases = release && !earlyThrow ? 1 : 0;
                        if (!caught || renderer.Releases != releases + expectedReleases ||
                            RuntimeMaterial::Destroyed != destroyed + 1) return 2;
                    }
                }
            }
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
    }
}
