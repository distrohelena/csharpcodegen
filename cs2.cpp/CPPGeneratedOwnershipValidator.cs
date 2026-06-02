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
            if (!RequiresGeneratedRuntimeComponentDeserializerRegistration(source, header)) {
                return;
            }

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
            bool requiresPendingSceneOperationDisposal = RequiresPendingSceneOperationDisposal(source);
            bool requiresTransientSceneLoadResultDisposal = RequiresTransientSceneLoadResultDisposal(source);
            bool requiresLoadedSceneRecordDisposal = RequiresLoadedSceneRecordDisposal(source);
            if (requiresPendingSceneOperationDisposal) {
                RequireContains(sourcePath, source, "delete operation;", "pending scene operation disposal");
            }

            if (requiresTransientSceneLoadResultDisposal) {
                RequireContains(sourcePath, source, "delete loadResult;", "transient scene load result disposal");
            }

            if (requiresLoadedSceneRecordDisposal) {
                RequireContains(sourcePath, source, "delete loadedSceneRecord;", "loaded scene record disposal");
            }

            if (requiresPendingSceneOperationDisposal ||
                requiresTransientSceneLoadResultDisposal ||
                requiresLoadedSceneRecordDisposal) {
                RequireContains(sourcePath, source, "he_cpp_make_scope_exit", "scope-exit ownership guards");
            }
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
            if (RequiresTransientAssetScopeGuards(source)) {
                RequireContains(sourcePath, source, "he_cpp_make_scope_exit", "transient asset scope guards");
            }
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
            if (!RequiresFontAssetSourceTextureOwnership(source)) {
                return;
            }

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

        /// <summary>
        /// Returns whether the generated runtime scene resolver still materializes transient raw model or texture assets that need scope-exit cleanup.
        /// </summary>
        /// <param name="contents">Generated runtime scene resolver source.</param>
        /// <returns><c>true</c> when transient raw asset scope guards are still required; otherwise <c>false</c>.</returns>
        static bool RequiresTransientAssetScopeGuards(string contents) {
            if (string.IsNullOrWhiteSpace(contents)) {
                return false;
            }

            return contents.Contains("BuildTextureFromRaw(", StringComparison.Ordinal)
                || contents.Contains("BuildModelFromRaw(", StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns whether the generated runtime component registry still relies on the generated deserializer-registration hook.
        /// </summary>
        /// <param name="source">Generated runtime component registry source.</param>
        /// <param name="header">Generated runtime component registry header.</param>
        /// <returns><c>true</c> when the free-function registration contract is present; otherwise <c>false</c>.</returns>
        static bool RequiresGeneratedRuntimeComponentDeserializerRegistration(string source, string header) {
            if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(header)) {
                return false;
            }

            return (source?.Contains("RegisterGeneratedRuntimeComponentDeserializers(", StringComparison.Ordinal) ?? false)
                || (header?.Contains("RegisterGeneratedRuntimeComponentDeserializers(", StringComparison.Ordinal) ?? false);
        }

        /// <summary>
        /// Returns whether the generated scene manager contains queued-operation ownership that must be validated.
        /// </summary>
        /// <param name="contents">Generated scene manager source.</param>
        /// <returns><c>true</c> when queued scene operations are present; otherwise <c>false</c>.</returns>
        static bool RequiresPendingSceneOperationDisposal(string contents) {
            if (string.IsNullOrWhiteSpace(contents)) {
                return false;
            }

            return contents.Contains("PendingSceneOperation", StringComparison.Ordinal)
                || contents.Contains("SceneOperation", StringComparison.Ordinal)
                || contents.Contains("ApplyPending", StringComparison.Ordinal)
                || contents.Contains("FlushPending", StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns whether the generated scene manager materializes transient load results that require explicit cleanup.
        /// </summary>
        /// <param name="contents">Generated scene manager source.</param>
        /// <returns><c>true</c> when transient scene-load results are present; otherwise <c>false</c>.</returns>
        static bool RequiresTransientSceneLoadResultDisposal(string contents) {
            if (string.IsNullOrWhiteSpace(contents)) {
                return false;
            }

            return contents.Contains("loadResult", StringComparison.Ordinal)
                || contents.Contains("LoadSceneImmediate(", StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns whether the generated scene manager materializes loaded-scene tracking records that require explicit cleanup.
        /// </summary>
        /// <param name="contents">Generated scene manager source.</param>
        /// <returns><c>true</c> when loaded-scene tracking records are present; otherwise <c>false</c>.</returns>
        static bool RequiresLoadedSceneRecordDisposal(string contents) {
            if (string.IsNullOrWhiteSpace(contents)) {
                return false;
            }

            return contents.Contains("loadedSceneRecord", StringComparison.Ordinal)
                || contents.Contains("LoadedSceneRecord", StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns whether the generated font asset still materializes one transient source texture asset that must be deleted.
        /// </summary>
        /// <param name="contents">Generated font asset source.</param>
        /// <returns><c>true</c> when transient source-texture ownership is present; otherwise <c>false</c>.</returns>
        static bool RequiresFontAssetSourceTextureOwnership(string contents) {
            if (string.IsNullOrWhiteSpace(contents)) {
                return false;
            }

            return contents.Contains("sourceTextureAsset", StringComparison.Ordinal);
        }
    }
}
