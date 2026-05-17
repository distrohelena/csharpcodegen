namespace cs2.cpp {
    /// <summary>
    /// Applies post-generation output rewrites required by one platform math convention.
    /// </summary>
    public sealed class CPPGeneratedOutputAdapter {
        /// <summary>
        /// Applies any required post-processing to one generated output root.
        /// </summary>
        public void Apply(string outputFolder, CPPConversionOptions options) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            } else if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            CPPGeneratedSourcePruner.RemoveEditorOnlyGeneratedSourceFiles(outputFolder);

            if (options.PlatformProfile.Kind == CPPPlatformKind.GameCubeHeadless) {
                new CPPGameCubeGeneratedRuntimeAdapter().Apply(outputFolder);
            }

            if (options.PlatformProfile.GeneratedMathConvention == CPPGeneratedMathConventionKind.NativeColumnVector
                && options.PlatformProfile.Kind == CPPPlatformKind.GameCubeHeadless) {
                new CPPGameCubeGeneratedMathAdapter().Apply(outputFolder);
            }
        }
    }
}
