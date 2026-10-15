using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Verifies standalone cast consumers include complete external source types obtained indirectly through getters and method calls.</summary>
public sealed class CPPPolymorphicCastSourceIncludeTests {
    /// <summary>Converts two real projects and writes a native harness whose consumer translation unit must compile independently of the external implementations.</summary>
    /// <param name="usePs1">Selects the real PS1/PsyQo provider profile for SDK compilation instead of the portable host provider.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOutput_ExternalIndirectCastSources_IncludeCompleteSourceAndTargetTypes(bool usePs1) {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-ps1/menu-20261007/scene-modules/cast-source-include-tests";
        root = Path.Combine(root, usePs1 ? "ps1" : "freestanding");
        string external = Path.Combine(root, "External");
        string consumer = Path.Combine(root, "Consumer");
        Directory.CreateDirectory(external);
        Directory.CreateDirectory(consumer);
        File.Delete(Path.Combine(external, "CastTarget.cs"));
        Dictionary<string, string> sources = new() {
            ["ICastSource.cs"] = "public interface ICastSource { int Value { get; } }",
            ["ICastTarget.cs"] = "public interface ICastTarget { int Value { get; } }",
            ["IGenericCastTarget.cs"] = "public interface IGenericCastTarget { }",
            ["CastMatch.cs"] = "public class CastMatch : ICastSource, ICastTarget, IGenericCastTarget { public int Value { get { return 73; } } }",
            ["CastOther.cs"] = "public class CastOther : ICastSource { public int Value { get { return 11; } } }",
            ["CastNode.cs"] = "public class CastNode { public ICastSource Current { get; set; } public IGenericCastTarget GenericCurrent { get; set; } public ICastSource Resolve() { return Current; } }",
            ["GenericCastHelper.cs"] = "public static class GenericCastHelper { public static T GenericCast<T>(ICastSource source) where T : class { return source as T; } public static T InferredCast<T>(ICastSource source, T witness) where T : class { return source as T; } }",
            ["CastProvider.cs"] = "public class CastProvider { public CastNode Node { get; set; } public CastNode ResolveNode() { return Node; } }"
        };
        foreach (KeyValuePair<string, string> source in sources) { File.WriteAllText(Path.Combine(external, source.Key), source.Value); }
        string externalProject = Path.Combine(external, "External.csproj");
        File.WriteAllText(externalProject, CreateProject(string.Empty));
        string assemblyPath = Path.Combine(external, "External.dll");
        CSharpCompilation compilation = CSharpCompilation.Create("External",
            sources.Values.Select(source => CSharpSyntaxTree.ParseText(source)),
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Microsoft.CodeAnalysis.Emit.EmitResult assembly = compilation.Emit(assemblyPath);
        Assert.True(assembly.Success, string.Join(Environment.NewLine, assembly.Diagnostics));
        string consumerProject = Path.Combine(consumer, "Consumer.csproj");
        File.WriteAllText(consumerProject, CreateProject("<Reference Include=\"External\"><HintPath>../External/External.dll</HintPath></Reference>"));
        File.WriteAllText(Path.Combine(consumer, "CastConsumer.cs"), """
            public class CastConsumer {
                public int Pattern(CastProvider provider) {
                    return provider.Node.Current is ICastTarget target ? target.Value : -7;
                }
                public int AsCast(CastProvider provider) {
                    ICastTarget target = provider.ResolveNode().Resolve() as ICastTarget;
                    return target != null ? target.Value : -7;
                }
                public int ExplicitCast(CastProvider provider) {
                    return ((ICastTarget)provider.Node.Resolve()).Value;
                }
                public bool TypeTest(CastProvider provider) {
                    return provider.ResolveNode().Current is ICastTarget;
                }
            }
            """);
        File.WriteAllText(Path.Combine(consumer, "GenericCastConsumer.cs"), """
            public class GenericCastConsumer {
                public bool ExplicitGeneric(CastProvider provider) {
                    return GenericCastHelper.GenericCast<IGenericCastTarget>(provider.Node.Current) != null;
                }
                public bool InferredGeneric(CastProvider provider) {
                    return GenericCastHelper.InferredCast(provider.ResolveNode().Resolve(), provider.Node.GenericCurrent) != null;
                }
            }
            """);
        string externalOutput = Path.Combine(root, "external-generated");
        string consumerOutput = Path.Combine(root, "consumer-generated");
        ConvertProject(externalProject, externalOutput, usePs1);
        ConvertProject(consumerProject, consumerOutput, usePs1);
        string sourceText = File.ReadAllText(Path.Combine(consumerOutput, "CastConsumer.cpp"));
        Assert.Contains("#include \"ICastSource.hpp\"", sourceText);
        Assert.Contains("#include \"ICastTarget.hpp\"", sourceText);
        Assert.DoesNotContain("#include \"ICastSource.hpp\"", File.ReadAllText(Path.Combine(externalOutput, "ICastTarget.hpp")));
        string header = File.ReadAllText(Path.Combine(consumerOutput, "CastConsumer.hpp"));
        Assert.DoesNotContain("ICastSource*", header);
        string genericSource = File.ReadAllText(Path.Combine(consumerOutput, "GenericCastConsumer.cpp"));
        Assert.Contains("#include \"IGenericCastTarget.hpp\"", genericSource);
        Assert.DoesNotContain("#include \"IGenericCastTarget.hpp\"", File.ReadAllText(Path.Combine(externalOutput, "GenericCastHelper.hpp")));
        Assert.DoesNotContain("#include \"IGenericCastTarget.hpp\"", File.ReadAllText(Path.Combine(externalOutput, "CastProvider.hpp")));
        Assert.DoesNotContain("#include \"IGenericCastTarget.hpp\"", File.ReadAllText(Path.Combine(externalOutput, "CastNode.hpp")));
        Assert.DoesNotContain("IGenericCastTarget", File.ReadAllText(Path.Combine(consumerOutput, "GenericCastConsumer.hpp")));
        File.WriteAllText(Path.Combine(root, "native-harness.cpp"), """
            #include "CastConsumer.hpp"
            #include "CastProvider.hpp"
            #include "CastNode.hpp"
            #include "CastMatch.hpp"
            #include "CastOther.hpp"
            #include "GenericCastConsumer.hpp"
            #include <cstdlib>
            namespace he_cpp_custom {
                void* Allocate(size_t bytes) { return std::malloc(bytes); }
                void Free(void* memory) { std::free(memory); }
                [[noreturn]] void Fail(const char*) { std::abort(); }
                uint64_t MonotonicMicroseconds() { return 0; }
            }
            int main() {
                CastMatch target; CastOther other; CastNode node; CastProvider provider; CastConsumer consumer; GenericCastConsumer generic;
                provider.set_Node(&node);
                node.set_GenericCurrent(&target);
                node.set_Current(&target);
                if (consumer.Pattern(&provider) != 73 || consumer.AsCast(&provider) != 73 ||
                    consumer.ExplicitCast(&provider) != 73 || !consumer.TypeTest(&provider)) return 1;
                if (!generic.ExplicitGeneric(&provider) || !generic.InferredGeneric(&provider)) return 4;
                node.set_Current(&other);
                if (consumer.Pattern(&provider) != -7 || consumer.AsCast(&provider) != -7 || consumer.TypeTest(&provider)) return 2;
                if (generic.ExplicitGeneric(&provider) || generic.InferredGeneric(&provider)) return 5;
                node.set_Current(nullptr);
                if (consumer.Pattern(&provider) != -7 || consumer.AsCast(&provider) != -7 || consumer.TypeTest(&provider)) return 3;
                if (generic.ExplicitGeneric(&provider) || generic.InferredGeneric(&provider)) return 6;
                node.set_GenericCurrent(nullptr);
                node.set_Current(&target);
                if (!generic.InferredGeneric(&provider)) return 7;
                return 0;
            }
            """);
    }

    /// <summary>Creates the small project files loaded by the actual converter, with an external metadata reference on the consumer.</summary>
    static string CreateProject(string references) {
        return "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup><ItemGroup>"
            + references + "</ItemGroup></Project>";
    }

    /// <summary>Runs the complete converter with the freestanding fatal-only runtime and verifies its report before native compilation.</summary>
    static void ConvertProject(string project, string output, bool usePs1) {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.RuntimeProfile = usePs1 ? CPPRuntimeProfile.CreateStlLite() : CPPRuntimeProfile.CreateFreestanding();
        options.RuntimeProfile.UseRtti = true;
        Dictionary<string, string> platformValues = new Dictionary<string, string> {
            ["codegen-header-template-definitions"] = "true"
        };
        if (usePs1) {
            options.CompilerProfile = CPPCompilerProfile.CreateGcc();
            options.PlatformProfile = CPPPlatformProfile.CreateCustomHeadless("ps1", true, CPPGeneratedMathConventionKind.EngineRowVector, 4);
            options.RuntimeProfile.UseStdString = false;
            options.RuntimeProfile.UseStdVector = false;
            options.RuntimeProfile.UseStdUnorderedMap = false;
            options.RuntimeProfile.UseStdUnorderedSet = false;
            options.RuntimeProfile.UseStdFunction = false;
            options.RuntimeProfile.UseStdChrono = false;
            options.RuntimeProfile.UseStdSharedPtr = false;
            options.RuntimeProfile.UseStdMath = false;
            options.RuntimeProfile.UseHostedFileSystem = false;
            options.RuntimeProfile.UseExceptions = false;
            platformValues[CPPCodegenOptionNames.RuntimeProviderHeader] = "Ps1RuntimeProvider.hpp";
            platformValues[CPPCodegenOptionNames.RuntimeMathHeader] = "Ps1Math.hpp";
        }
        options.PlatformOptionValues = platformValues;
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.AddCsproj(project);
        converter.WriteOutput(output);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean(), report.RootElement.GetRawText());
    }
}
