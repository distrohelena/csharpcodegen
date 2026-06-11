namespace cs2.cpp {
/// <summary>
/// Applies generic post-generation cleanup after runtime files are emitted.
/// </summary>
public sealed class CPPGeneratedOutputAdapter {
    /// <summary>
    /// Applies generic post-processing to one generated output root.
    /// </summary>
    public void Apply(string outputFolder, CPPConversionOptions options) {
        if (string.IsNullOrWhiteSpace(outputFolder)) {
            throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
        } else if (options == null) {
            throw new ArgumentNullException(nameof(options));
        }

        CPPGeneratedSourcePruner.RemoveEditorOnlyGeneratedSourceFiles(outputFolder);
    }
}
}
