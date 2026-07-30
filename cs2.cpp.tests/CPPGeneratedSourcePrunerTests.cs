namespace cs2.cpp.tests;

/// <summary>
/// Verifies that authoring-only generated types are excluded from directly compilable native runtime output.
/// </summary>
public class CPPGeneratedSourcePrunerTests {
    /// <summary>
    /// Ensures the native migration marker attribute is removed because its managed attribute base has no native runtime representation.
    /// </summary>
    [Fact]
    public void RemoveEditorOnlyGeneratedSourceFiles_WithNativeMigrationMarker_RemovesEveryGeneratedSourceExtension() {
        string outputPath = Path.Combine(Path.GetTempPath(), "cs2cpp-generated-source-pruner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        string headerPath = Path.Combine(outputPath, "NativeMigrationRequiredAttribute.hpp");
        string sourcePath = Path.Combine(outputPath, "NativeMigrationRequiredAttribute.cpp");
        string templatePath = Path.Combine(outputPath, "NativeMigrationRequiredAttribute.tpp");
        File.WriteAllText(headerPath, "// header");
        File.WriteAllText(sourcePath, "// source");
        File.WriteAllText(templatePath, "// template");

        CPPGeneratedSourcePruner.RemoveEditorOnlyGeneratedSourceFiles(outputPath);

        Assert.False(File.Exists(headerPath));
        Assert.False(File.Exists(sourcePath));
        Assert.False(File.Exists(templatePath));
    }

    /// <summary>
    /// Ensures the scene persistence append marker is removed because it is consumed only by managed authoring and reflection pipelines.
    /// </summary>
    [Fact]
    public void RemoveEditorOnlyGeneratedSourceFiles_WithScenePersistenceAppendMarker_RemovesEveryGeneratedSourceExtension() {
        string outputPath = Path.Combine(Path.GetTempPath(), "cs2cpp-generated-source-pruner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        string headerPath = Path.Combine(outputPath, "ScenePersistenceAppendAttribute.hpp");
        string sourcePath = Path.Combine(outputPath, "ScenePersistenceAppendAttribute.cpp");
        string templatePath = Path.Combine(outputPath, "ScenePersistenceAppendAttribute.tpp");
        File.WriteAllText(headerPath, "// header");
        File.WriteAllText(sourcePath, "// source");
        File.WriteAllText(templatePath, "// template");

        CPPGeneratedSourcePruner.RemoveEditorOnlyGeneratedSourceFiles(outputPath);

        Assert.False(File.Exists(headerPath));
        Assert.False(File.Exists(sourcePath));
        Assert.False(File.Exists(templatePath));
    }
}
