using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>Verifies separately converted C# assemblies share the same ref/out method names across source and metadata project boundaries.</summary>
public sealed class CPPExternalMethodAbiTests {
    /// <summary>Checks external static and instance calls against the independently generated producer headers.</summary>
    /// <param name="useMetadataReference">Uses a compiled DLL instead of the producer's project-reference source symbols.</param>
    /// <param name="loadNativeRuntimeMetadata">Includes native runtime mappings to protect the separate native ABI path.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void WriteOutput_WithExternalRefOutMethods_MatchesProducerAbi(bool useMetadataReference, bool loadNativeRuntimeMetadata) {
        string scratchRoot = Path.GetFullPath(Path.GetTempPath());
        string fixtureRoot = Path.GetFullPath(Path.Combine(scratchRoot, "external-method-abi"));
        if (!fixtureRoot.StartsWith(scratchRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("The external ABI fixture must remain within the configured temporary root.");
        }
        Directory.CreateDirectory(fixtureRoot);
        try {
            string engineDirectory = Path.Combine(fixtureRoot, "Engine");
            string consumerDirectory = Path.Combine(fixtureRoot, "Consumer");
            Directory.CreateDirectory(engineDirectory);
            Directory.CreateDirectory(consumerDirectory);
            string mathSource = """
                namespace Engine;
                public static class SharedRotationMath {
                    public static bool TryBuildRay(int pointer, out int origin, out int direction) { origin = pointer; direction = -1; return true; }
                    public static bool TryIntersect(int ray, int plane, out int point) { point = ray + plane; return true; }
                    public static bool TryPick(int ray, int flags, out int axis, out int distance) { axis = flags; distance = ray; return true; }
                    public static int Step(ref int value) { value++; return value; }
                    public static int Step(int value) { return value + 1; }
                }
                """;
            string solverSource = """
                namespace Engine;
                public sealed class SharedRotationSolver {
                    public bool Solve(int value, out int solved) { solved = value + 2; return true; }
                }
                """;
            File.WriteAllText(Path.Combine(engineDirectory, "SharedRotationMath.cs"), mathSource);
            File.WriteAllText(Path.Combine(engineDirectory, "SharedRotationSolver.cs"), solverSource);
            string engineProject = Path.Combine(engineDirectory, "Engine.csproj");
            File.WriteAllText(engineProject, CreateProject(""));
            CPPCodeConverter producer = CreateConverter(loadNativeRuntimeMetadata);
            producer.AddCsproj(engineProject);
            string producerOutput = Path.Combine(fixtureRoot, "producer");
            producer.WriteOutput(producerOutput);
            string producerHeader = File.ReadAllText(Path.Combine(producerOutput, "SharedRotationMath.hpp"));
            Assert.Contains("TryBuildRay__out1_out2(", producerHeader, StringComparison.Ordinal);
            Assert.Contains("TryIntersect__out2(", producerHeader, StringComparison.Ordinal);
            Assert.Contains("TryPick__out2_out3(", producerHeader, StringComparison.Ordinal);
            Assert.Contains("Step__ref0(", producerHeader, StringComparison.Ordinal);
            string references;
            if (useMetadataReference) {
                WriteReferenceAssembly(Path.Combine(engineDirectory, "Engine.dll"), mathSource, solverSource);
                references = "<Reference Include=\"Engine\"><HintPath>../Engine/Engine.dll</HintPath></Reference>";
            } else {
                references = "<ProjectReference Include=\"../Engine/Engine.csproj\" />";
            }
            string consumerProject = Path.Combine(consumerDirectory, "Consumer.csproj");
            File.WriteAllText(consumerProject, CreateProject(references));
            File.WriteAllText(Path.Combine(consumerDirectory, "RotationConsumer.cs"), """
                using Engine;
                using System.Collections.Generic;
                using static Engine.SharedRotationMath;
                public class RotationConsumer {
                    public int Run(SharedRotationSolver solver) {
                        SharedRotationMath.TryBuildRay(7, out int origin, out int direction);
                        SharedRotationMath.TryIntersect(origin, direction, out int point);
                        SharedRotationMath.TryPick(point, 2, out int axis, out int distance);
                        int value = axis;
                        SharedRotationMath.Step(ref value);
                        solver.Solve(distance, out int solved);
                        return SharedRotationMath.Step(value) + solved;
                    }
                    public int RunImported(int value) {
                        Step(ref value);
                        return value;
                    }
                    public bool Parse(string text, out int value) {
                        return int.TryParse(text, out value);
                    }
                    public bool Find(Dictionary<int, int> entries, int key, out int value) {
                        return entries.TryGetValue(key, out value);
                    }
                    public bool FindReadOnly(IReadOnlyDictionary<int, int> readOnlyEntries, int key, out int value) {
                        return readOnlyEntries.TryGetValue(key, out value);
                    }
                }
                """);
            CPPCodeConverter consumer = CreateConverter(loadNativeRuntimeMetadata);
            consumer.AddCsproj(consumerProject);
            string consumerOutput = Path.Combine(fixtureRoot, "consumer");
            consumer.WriteOutput(consumerOutput);
            string source = File.ReadAllText(Path.Combine(consumerOutput, "RotationConsumer.cpp"));
            Assert.True(source.Contains("SharedRotationMath::TryBuildRay__out1_out2(", StringComparison.Ordinal), source);
            Assert.Contains("SharedRotationMath::TryIntersect__out2(", source, StringComparison.Ordinal);
            Assert.Contains("SharedRotationMath::TryPick__out2_out3(", source, StringComparison.Ordinal);
            Assert.Contains("SharedRotationMath::Step__ref0(value)", source, StringComparison.Ordinal);
            Assert.Contains("solver->Solve__out1(", source, StringComparison.Ordinal);
            Assert.Contains("SharedRotationMath::Step(", source, StringComparison.Ordinal);
            string importedCall = source.Substring(source.IndexOf("RotationConsumer::RunImported(", StringComparison.Ordinal));
            Assert.Contains("Step__ref0(", importedCall, StringComparison.Ordinal);
            Assert.Contains("Number::TryParse(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Number::TryParse__", source, StringComparison.Ordinal);
            Assert.Contains("entries->TryGetValue(", source, StringComparison.Ordinal);
            Assert.Contains("readOnlyEntries->TryGetValue(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("entries->TryGetValue__", source, StringComparison.Ordinal);
            Assert.DoesNotContain("readOnlyEntries->TryGetValue__", source, StringComparison.Ordinal);
        } finally {
            Directory.Delete(fixtureRoot, true);
        }
    }

    /// <summary>Creates a minimal SDK project preserving the actual source-project or DLL reference boundary.</summary>
    /// <param name="references">External project or assembly reference XML.</param>
    /// <returns>Complete project definition consumed by the production loader.</returns>
    static string CreateProject(string references) {
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
              <ItemGroup>{{references}}</ItemGroup>
            </Project>
            """;
    }

    /// <summary>Creates an independent production converter for one side of the ABI boundary.</summary>
    /// <param name="loadNativeRuntimeMetadata">Whether runtime-native type mappings participate in name resolution.</param>
    /// <returns>Converter configured for the requested mapping mode.</returns>
    static CPPCodeConverter CreateConverter(bool loadNativeRuntimeMetadata) {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = loadNativeRuntimeMetadata;
        return new CPPCodeConverter(new CPPConversionRules(), options);
    }

    /// <summary>Emits the producer DLL directly with Roslyn so consumer binding has metadata rather than source declarations.</summary>
    /// <param name="assemblyPath">Fixed temporary assembly destination.</param>
    /// <param name="mathSource">Producer's static ref/out helper implementation.</param>
    /// <param name="solverSource">Producer's instance-method implementation.</param>
    static void WriteReferenceAssembly(string assemblyPath, string mathSource, string solverSource) {
        CSharpCompilation compilation = CSharpCompilation.Create("Engine",
            new[] { CSharpSyntaxTree.ParseText(mathSource), CSharpSyntaxTree.ParseText(solverSource) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(assemblyPath);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
