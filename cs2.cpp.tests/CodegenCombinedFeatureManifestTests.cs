using codegen;
using System.Text.Json.Nodes;

namespace cs2.cpp.tests;

/// <summary>Exercises the actual manifest-only CLI and shared producer with strict aggregate report validation.</summary>
public sealed class CodegenCombinedFeatureManifestTests {
    /// <summary>Visible fixed fixture root that retains emitted files for independent native compilation.</summary>
    const string FixtureRoot = "C:/dev/helworks/builds/helengine-ps1/menu-20261007/scene-modules/feature-cli-tests";

    /// <summary>The actual engine catalog used by the official scene build.</summary>
    const string CatalogPath = "C:/dev/helworks/helengine/engine/helengine.editor/codegen/features/helengine-feature-catalog.json";

    /// <summary>Checks one real CLI invocation emits union decisions through both runtime query data and compile-time defines without changing ABI or gameplay files.</summary>
    [Fact]
    public void Execute_AggregateDecisions_UsesOneProducerAndPreservesOtherFiles() {
        string root = Path.Combine(FixtureRoot, "valid");
        Directory.CreateDirectory(root);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PlatformOptionValues = new Dictionary<string, string> { ["scene-code-modules"] = "true" };
        CPPGeneratedConfigWriter.Write(root, options, new CPPRuntimeRequirementRegistrar(new CPPRuntimeRequirementCatalog(), new CPPConversionReport()), new CPPBuildUsageReport());
        string configPath = Path.Combine(root, "helcpp_config.hpp");
        string config = File.ReadAllText(configPath);
        Assert.Contains("#include \"runtime/feature_config.hpp\"", config);
        Assert.DoesNotContain("#define HE_CPP_FEATURE_", config);
        string gameplay = Path.Combine(root, "Gameplay.cpp");
        File.WriteAllText(gameplay, "int preserve_gameplay = 123;");
        string reportPath = Path.Combine(root, "cpp-conversion-report.json");
        File.WriteAllText(reportPath, CreateReport().ToJsonString());
        Assert.Equal(0, Program.Execute(["--write-feature-manifest", "--report", reportPath,
            "--feature-catalog", CatalogPath, "--output", root]));
        Assert.Equal(config, File.ReadAllText(configPath));
        Assert.Equal("int preserve_gameplay = 123;", File.ReadAllText(gameplay));
        string manifest = File.ReadAllText(Path.Combine(root, "runtime/feature_manifest.cpp"));
        string features = File.ReadAllText(Path.Combine(root, "runtime/feature_config.hpp"));
        foreach (string id in new[] { "render2d", "sprites", "text2d" }) {
            Assert.Contains($"HEFeature::{CPPFeatureIdentifierFormatter.ToEnumMemberName(id)}, true, HEFeatureDecisionOrigin::AutoDetected", manifest);
            Assert.Contains($"#define HE_CPP_FEATURE_{CPPFeatureIdentifierFormatter.ToConfigDefineSuffix(id)} 1", features);
        }
        Assert.Contains("HEFeature::Shaders, false, HEFeatureDecisionOrigin::ForcedDisabled", manifest);
        Assert.Contains("#define HE_CPP_FEATURE_SHADERS 0", features);
        File.WriteAllText(Path.Combine(root, "native-harness.cpp"), """
            #include "runtime/feature_manifest.hpp"
            #include "runtime/feature_config.hpp"
            #include <cassert>
            static_assert(HE_CPP_FEATURE_RENDER2D == 1);
            static_assert(HE_CPP_FEATURE_SPRITES == 1);
            static_assert(HE_CPP_FEATURE_TEXT2D == 1);
            static_assert(HE_CPP_FEATURE_SHADERS == 0);
            int main() {
                assert(he_feature_enabled(HEFeature::Render2d));
                assert(he_feature_enabled(HEFeature::Sprites));
                assert(he_feature_enabled(HEFeature::Text2d));
                assert(!he_feature_enabled(HEFeature::Shaders));
                return 0;
            }
            """);
    }

    /// <summary>Rejects malformed or incomplete commands/reports before touching any existing native manifest.</summary>
    /// <param name="failure">Invalid input kind exercised through the actual CLI entry point.</param>
    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("origin")]
    [InlineData("boolean")]
    [InlineData("json")]
    [InlineData("arguments")]
    [InlineData("legacy")]
    public void Execute_InvalidReport_FailsWithoutChangingOutput(string failure) {
        string root = Path.Combine(FixtureRoot, failure);
        Directory.CreateDirectory(Path.Combine(root, "runtime"));
        File.WriteAllText(Path.Combine(root, "helcpp_config.hpp"), failure == "legacy" ?
            "#define HE_CPP_FEATURE_RENDER2D 0" : "#include \"runtime/feature_config.hpp\"");
        string output = Path.Combine(root, "runtime/feature_manifest.cpp");
        File.WriteAllText(output, "unchanged");
        JsonObject report = CreateReport();
        JsonArray decisions = report["buildFeatures"]["decisions"].AsArray();
        if (failure == "unknown") decisions[0]["feature"] = "unknown_feature";
        else if (failure == "duplicate") decisions.Add(decisions[0].DeepClone());
        else if (failure == "missing") decisions.RemoveAt(0);
        else if (failure == "origin") decisions[0]["origin"] = "0";
        else if (failure == "boolean") decisions[0]["enabled"] = "true";
        string reportPath = Path.Combine(root, "report.json");
        File.WriteAllText(reportPath, failure == "json" ? "{" : report.ToJsonString());
        string[] arguments = failure == "arguments" ? ["--write-feature-manifest"] :
            ["--write-feature-manifest", "--report", reportPath, "--feature-catalog", CatalogPath, "--output", root];
        Assert.NotEqual(0, Program.Execute(arguments));
        Assert.Equal("unchanged", File.ReadAllText(output));
    }

    /// <summary>Creates a complete report using real catalog ids, with only the scene-required rendering features enabled.</summary>
    /// <returns>Aggregate feature decisions expected from the official multi-project report composer.</returns>
    static JsonObject CreateReport() {
        JsonArray decisions = new();
        foreach (CPPExternalFeatureDefinition feature in CPPExternalFeatureCatalogLoader.LoadFromFile(CatalogPath).Features) {
            bool enabled = feature.Id is "render2d" or "sprites" or "text2d";
            decisions.Add(new JsonObject { ["feature"] = feature.Id, ["enabled"] = enabled,
                ["origin"] = enabled ? "AutoDetected" : feature.Id == "shaders" ? "ForcedDisabled" : "NotIncluded" });
        }
        return new JsonObject { ["buildFeatures"] = new JsonObject { ["decisions"] = decisions } };
    }
}
