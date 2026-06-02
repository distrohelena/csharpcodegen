using cs2.core;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.cpp.tests;

/// <summary>
/// Covers feature-pruned reachable type selection before C++ emission.
/// </summary>
public class CPPReachabilityPlannerTests {
    /// <summary>
    /// Verifies that disabled shader features remove shader-tagged types from the reachable plan.
    /// </summary>
    [Fact]
    public void Build_WhenShadersAreDisabled_ExcludesShaderTypesFromReachableSet() {
        ConversionProgram program = CreateProgram("""
namespace helengine {
    public class ShaderAsset {
    }
}

namespace helengine.core.scene {
    public class SceneNode {
    }
}
""");

        CPPBuildFeatureProfile profile = CPPBuildFeatureProfile.CreateDefault()
            .WithMode("shaders", CPPFeatureMode.Disabled);
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateHelengineCatalog();
        CPPBuildUsageReport report = CPPFeatureResolver.Resolve(
            profile,
            catalog,
            new[] {
                new CPPFeatureUsageRoot {
                    FeatureId = "shaders",
                    RootId = "helengine.core.shaders.ShaderAsset",
                    SourceKind = "TypeReference",
                }
            });

        CPPReachabilityPlan plan = CPPReachabilityPlanner.Build(program, report, catalog);

        Assert.DoesNotContain(plan.Types, type => type.TypeSymbol?.ToDisplayString() == "helengine.ShaderAsset");
        Assert.Contains(plan.Types, type => type.TypeSymbol?.ToDisplayString() == "helengine.core.scene.SceneNode");
    }

    /// <summary>
    /// Builds a conversion program from synthetic source so planner tests can use real Roslyn type symbols.
    /// </summary>
    /// <param name="source">The source text to compile into the synthetic program.</param>
    /// <returns>A conversion program populated with the declared classes from the source.</returns>
    static ConversionProgram CreateProgram(string source) {
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        ConversionProgram program = new ConversionProgram(new CPPConversionRules());
        CompilationUnitSyntax root = (CompilationUnitSyntax)compilation.SyntaxTrees[0].GetRoot();
        SemanticModel semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees[0]);

        foreach (ClassDeclarationSyntax classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>()) {
            program.Classes.Add(new ConversionClass {
                Name = classDeclaration.Identifier.Text,
                DeclarationType = MemberDeclarationType.Class,
                Semantic = semanticModel,
                TypeSymbol = semanticModel.GetDeclaredSymbol(classDeclaration),
            });
        }

        return program;
    }
}
