namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Creates reusable external feature catalogs for focused generic codegen tests.
/// </summary>
public static class CPPTestFeatureCatalogFactory {
    /// <summary>
    /// Creates one sample feature catalog used by generic feature-scanning and pruning tests.
    /// </summary>
    /// <returns>External feature catalog populated with caller-owned sample type roots.</returns>
    public static CPPExternalFeatureCatalog CreateSampleFeatureCatalog() {
        return CPPExternalFeatureCatalogLoader.LoadFromJson("""
{
  "features": [
    { "id": "render2d", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "sprites", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "text2d", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "shaders", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "debug_overlay", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "runtime_json", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "reflection_like_runtime", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "host_file_system", "defaultMode": "auto", "conflictPolicy": "error" },
    { "id": "text_processing", "defaultMode": "auto", "conflictPolicy": "error" }
  ],
  "rootRules": [
    { "typeName": "ExampleEngine.RenderManager2D", "featureIds": [ "render2d" ] },
    { "typeName": "ExampleEngine.IDrawable2D", "featureIds": [ "render2d" ] },
    { "typeName": "ExampleEngine.ISpriteDrawable2D", "featureIds": [ "sprites", "render2d" ] },
    { "typeName": "ExampleEngine.Core.Graphics.ISpriteRenderable", "featureIds": [ "sprites", "render2d" ] },
    { "typeName": "ExampleEngine.SpriteComponent", "featureIds": [ "sprites", "render2d" ] },
    { "typeName": "ExampleEngine.ITextDrawable2D", "featureIds": [ "text2d", "render2d" ] },
    { "typeName": "ExampleEngine.TextComponent", "featureIds": [ "text2d", "render2d" ] },
    { "typeName": "ExampleEngine.DebugComponent", "featureIds": [ "debug_overlay" ] },
    { "typeName": "ExampleEngine.DebugOverlayComponent", "featureIds": [ "debug_overlay" ] },
    { "typeName": "ExampleEngine.ShaderAsset", "featureIds": [ "shaders", "text_processing" ] },
    { "typeName": "ExampleEngine.ShaderRuntimeMaterial", "featureIds": [ "shaders", "text_processing" ] },
    { "typeName": "ExampleEngine.IShaderRenderManager3D", "featureIds": [ "shaders", "text_processing" ] },
    { "typeName": "ExampleEngine.Core.Content.RuntimeManifestJsonReader", "featureIds": [ "runtime_json", "text_processing" ] },
    { "typeName": "ExampleEngine.TextContentProcessor", "featureIds": [ "text_processing" ] },
    { "typeName": "ExampleEngine.Core.Content.RuntimeStartupManifest", "featureIds": [ "runtime_json", "host_file_system" ] },
    { "typeName": "ExampleEngine.Core.Content.RuntimeCodeModuleManifest", "featureIds": [ "runtime_json", "host_file_system" ] },
    { "typeName": "ExampleEngine.ContentManager", "featureIds": [ "host_file_system", "text_processing" ] }
  ],
  "runtimeRequirements": [
    { "requirementId": "StringBuilder", "featureIds": [ "debug_overlay", "shaders", "text_processing" ] },
    { "requirementId": "Regex", "featureIds": [ "shaders", "text_processing" ] },
    { "requirementId": "StreamReader", "featureIds": [ "text_processing" ] },
    { "requirementId": "StringReader", "featureIds": [ "shaders", "text_processing" ] }
  ]
}
""");
    }
}
