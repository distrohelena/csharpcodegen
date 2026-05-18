namespace cs2.cpp {
    /// <summary>
    /// Validates generated C++ output for required ownership and native-integration contracts without mutating any emitted file.
    /// </summary>
    public static class CPPGeneratedOwnershipValidator {
        /// <summary>
        /// Validates one generated output folder and throws when a required native ownership contract is missing.
        /// </summary>
        /// <param name="outputFolder">Generated C++ output folder to validate.</param>
        public static void Validate(string outputFolder) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            ValidateRuntimeComponentRegistry(outputFolder);
            ValidateSceneManager(outputFolder);
            ValidateRuntimeSceneAssetReferenceResolver(outputFolder);
            ValidateRenderManager2D(outputFolder);
            ValidateFontAsset(outputFolder);
        }

        /// <summary>
        /// Validates the generated runtime component registry contracts used to bridge native-generated deserializer registration.
        /// </summary>
        /// <param name="outputFolder">Generated C++ output folder to validate.</param>
        static void ValidateRuntimeComponentRegistry(string outputFolder) {
            string sourcePath = Path.Combine(outputFolder, "RuntimeComponentRegistry.cpp");
            string headerPath = Path.Combine(outputFolder, "RuntimeComponentRegistry.hpp");
            if (!File.Exists(sourcePath) || !File.Exists(headerPath)) {
                return;
            }

            string source = File.ReadAllText(sourcePath);
            string header = File.ReadAllText(headerPath);
            RequireContains(sourcePath, source, "#include \"GeneratedRuntimeComponentDeserializerRegistration.hpp\"", "native free-function include for generated runtime component deserializer registration");
            RequireContains(sourcePath, source, "RegisterGeneratedRuntimeComponentDeserializers(registry);", "generated runtime component deserializer registration call");
            RequireDoesNotContain(headerPath, header, "RegisterGeneratedRuntimeComponentDeserializers(", "generated runtime component deserializer stub declaration");
        }

        /// <summary>
        /// Validates scene-manager ownership contracts for queued operations and transient scene-load results.
        /// </summary>
        /// <param name="outputFolder">Generated C++ output folder to validate.</param>
        static void ValidateSceneManager(string outputFolder) {
            string sourcePath = Path.Combine(outputFolder, "SceneManager.cpp");
            if (!File.Exists(sourcePath)) {
                return;
            }

            string source = File.ReadAllText(sourcePath);
            RequireContains(sourcePath, source, "delete operation;", "pending scene operation disposal");
            RequireContains(sourcePath, source, "delete loadResult;", "transient scene load result disposal");
            RequireContains(sourcePath, source, "delete loadedSceneRecord;", "loaded scene record disposal");
            RequireContains(sourcePath, source, "he_cpp_make_scope_exit", "scope-exit ownership guards");
        }

        /// <summary>
        /// Validates transient scene-asset resolver ownership contracts for generated native runtime assets.
        /// </summary>
        /// <param name="outputFolder">Generated C++ output folder to validate.</param>
        static void ValidateRuntimeSceneAssetReferenceResolver(string outputFolder) {
            string sourcePath = Path.Combine(outputFolder, "RuntimeSceneAssetReferenceResolver.cpp");
            if (!File.Exists(sourcePath)) {
                return;
            }

            string source = File.ReadAllText(sourcePath);
            RequireContains(sourcePath, source, "#include \"NativeOwnership.hpp\"", "native ownership helper include");
            RequireContains(sourcePath, source, "he_cpp_make_scope_exit", "transient asset scope guards");
        }

        /// <summary>
        /// Validates generated 2D render-manager ownership cleanup for transient fonts.
        /// </summary>
        /// <param name="outputFolder">Generated C++ output folder to validate.</param>
        static void ValidateRenderManager2D(string outputFolder) {
            string sourcePath = Path.Combine(outputFolder, "RenderManager2D.cpp");
            if (!File.Exists(sourcePath)) {
                return;
            }

            string source = File.ReadAllText(sourcePath);
            RequireContains(sourcePath, source, "#include \"NativeOwnership.hpp\"", "native ownership helper include");
            RequireContains(sourcePath, source, "delete font;", "transient font disposal");
        }

        /// <summary>
        /// Validates generated font-asset ownership cleanup for transient source textures.
        /// </summary>
        /// <param name="outputFolder">Generated C++ output folder to validate.</param>
        static void ValidateFontAsset(string outputFolder) {
            string sourcePath = Path.Combine(outputFolder, "FontAsset.cpp");
            if (!File.Exists(sourcePath)) {
                return;
            }

            string source = File.ReadAllText(sourcePath);
            RequireContains(sourcePath, source, "#include \"NativeOwnership.hpp\"", "native ownership helper include");
            RequireContains(sourcePath, source, "delete sourceTextureAsset;", "transient source texture disposal");
        }

        /// <summary>
        /// Throws when the supplied source text does not contain one required contract marker.
        /// </summary>
        /// <param name="filePath">File being validated.</param>
        /// <param name="contents">Current emitted file contents.</param>
        /// <param name="requiredText">Required contract marker text.</param>
        /// <param name="contractName">Human-readable contract description.</param>
        static void RequireContains(string filePath, string contents, string requiredText, string contractName) {
            if (!contents.Contains(requiredText, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Generated C++ output '{filePath}' is missing required contract '{contractName}'.");
            }
        }

        /// <summary>
        /// Throws when the supplied source text still contains one forbidden legacy stub marker.
        /// </summary>
        /// <param name="filePath">File being validated.</param>
        /// <param name="contents">Current emitted file contents.</param>
        /// <param name="forbiddenText">Forbidden legacy marker text.</param>
        /// <param name="contractName">Human-readable contract description.</param>
        static void RequireDoesNotContain(string filePath, string contents, string forbiddenText, string contractName) {
            if (contents.Contains(forbiddenText, StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Generated C++ output '{filePath}' still contains forbidden legacy contract '{contractName}'.");
            }
        }
    }
}
