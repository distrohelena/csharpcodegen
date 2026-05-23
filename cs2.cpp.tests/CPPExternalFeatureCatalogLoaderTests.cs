namespace cs2.cpp.tests;

/// <summary>
/// Covers loading and validating externally supplied feature metadata catalogs.
/// </summary>
public class CPPExternalFeatureCatalogLoaderTests {
    /// <summary>
    /// Verifies that the loader accepts free-form feature ids and preserves their feature, root-rule, and runtime requirement declarations.
    /// </summary>
    [Fact]
    public void Load_WhenCatalogUsesFreeFormFeatureIds_ParsesDefinitionsAndRootRules() {
        string json = """
{
  "features": [
    { "id": "shaders", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "render2d", "defaultMode": "auto", "conflictPolicy": "error" }
  ],
  "rootRules": [
    { "typeName": "example.Graphics.ShaderMaterial", "featureIds": [ "shaders" ] },
    { "typeName": "example.Graphics.Sprite", "featureIds": [ "render2d" ] }
  ],
  "runtimeRequirements": [
    { "requirementId": "Regex", "featureIds": [ "shaders" ] }
  ]
}
""";

        CPPExternalFeatureCatalog catalog = CPPExternalFeatureCatalogLoader.LoadFromJson(json);

        Assert.Contains(catalog.Features, feature => feature.Id == "shaders");
        Assert.Contains(catalog.RootRules, rule => rule.TypeName == "example.Graphics.ShaderMaterial");
        Assert.Contains(catalog.RuntimeRequirements, rule => rule.RequirementId == "Regex");
    }

    /// <summary>
    /// Verifies that a root rule cannot reference a feature id that the catalog never declared.
    /// </summary>
    [Fact]
    public void Load_WhenRootRuleReferencesUnknownFeature_Throws() {
        string json = """
{
  "features": [
    { "id": "render2d", "defaultMode": "auto", "conflictPolicy": "error" }
  ],
  "rootRules": [
    { "typeName": "example.Graphics.ShaderMaterial", "featureIds": [ "shaders" ] }
  ]
}
""";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CPPExternalFeatureCatalogLoader.LoadFromJson(json));

        Assert.Contains("shaders", exception.Message);
    }
}
