using Xunit;

namespace cs2.cpp.tests {
    /// <summary>
    /// Verifies the generated ownership validator fails loudly when required native contracts are missing.
    /// </summary>
    public sealed class CPPGeneratedOwnershipValidatorTests : IDisposable {
        /// <summary>
        /// Temporary output folder used by the validator tests.
        /// </summary>
        readonly string OutputFolder;

        /// <summary>
        /// Initializes one validator test workspace.
        /// </summary>
        public CPPGeneratedOwnershipValidatorTests() {
            OutputFolder = Path.Combine(Path.GetTempPath(), "cpp-generated-ownership-validator-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(OutputFolder);
        }

        /// <summary>
        /// Deletes the temporary validator test workspace.
        /// </summary>
        public void Dispose() {
            if (Directory.Exists(OutputFolder)) {
                Directory.Delete(OutputFolder, true);
            }
        }

        /// <summary>
        /// Verifies the validator throws when the runtime component registry does not include the generated registration header.
        /// </summary>
        [Fact]
        public void Validate_whenRuntimeComponentRegistryIsMissingGeneratedRegistrationHeader_throws() {
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeComponentRegistry.cpp"), "RegisterGeneratedRuntimeComponentDeserializers(registry);");
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeComponentRegistry.hpp"), "class RuntimeComponentRegistry { };");
            File.WriteAllText(Path.Combine(OutputFolder, "SceneManager.cpp"), "delete operation; delete loadResult; delete loadedSceneRecord; he_cpp_make_scope_exit");
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeSceneAssetReferenceResolver.cpp"), "#include \"NativeOwnership.hpp\"\nhe_cpp_make_scope_exit");
            File.WriteAllText(Path.Combine(OutputFolder, "RenderManager2D.cpp"), "#include \"NativeOwnership.hpp\"\ndelete font;");
            File.WriteAllText(Path.Combine(OutputFolder, "FontAsset.cpp"), "#include \"NativeOwnership.hpp\"\ndelete sourceTextureAsset;");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => CPPGeneratedOwnershipValidator.Validate(OutputFolder));

            Assert.Contains("RuntimeComponentRegistry.cpp", ex.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies cooked-platform-owned runtime scene resolvers are accepted without transient asset scope guards when no raw model or texture path remains.
        /// </summary>
        [Fact]
        public void Validate_whenRuntimeSceneResolverUsesCookedPlatformOwnedAssets_doesNotRequireScopeGuards() {
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeComponentRegistry.cpp"), "#include \"GeneratedRuntimeComponentDeserializerRegistration.hpp\"\nRegisterGeneratedRuntimeComponentDeserializers(registry);");
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeComponentRegistry.hpp"), "class RuntimeComponentRegistry { };");
            File.WriteAllText(Path.Combine(OutputFolder, "SceneManager.cpp"), "delete operation; delete loadResult; delete loadedSceneRecord; he_cpp_make_scope_exit");
            File.WriteAllText(
                Path.Combine(OutputFolder, "RuntimeSceneAssetReferenceResolver.cpp"),
                "#include \"NativeOwnership.hpp\"\n"
                + "RuntimeTexture* ResolveTexture() { return Core::get_Instance()->get_RenderManager2D()->BuildTextureFromCooked(fullPath); }\n"
                + "RuntimeModel* ResolveModel() { return Core::get_Instance()->get_RenderManager3D()->BuildModelFromCooked(fullPath); }\n");
            File.WriteAllText(Path.Combine(OutputFolder, "RenderManager2D.cpp"), "#include \"NativeOwnership.hpp\"\ndelete font;");
            File.WriteAllText(Path.Combine(OutputFolder, "FontAsset.cpp"), "#include \"NativeOwnership.hpp\"\ndelete sourceTextureAsset;");

            CPPGeneratedOwnershipValidator.Validate(OutputFolder);
        }

        /// <summary>
        /// Verifies non-engine scene-manager fixtures do not trigger engine ownership validation based only on the generated file name.
        /// </summary>
        [Fact]
        public void Validate_whenSceneManagerFixtureDoesNotUseEngineOwnershipContracts_doesNotRequireCleanupMarkers() {
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeComponentRegistry.cpp"), "#include \"GeneratedRuntimeComponentDeserializerRegistration.hpp\"\nRegisterGeneratedRuntimeComponentDeserializers(registry);");
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeComponentRegistry.hpp"), "class RuntimeComponentRegistry { };");
            File.WriteAllText(Path.Combine(OutputFolder, "SceneManager.cpp"), "std::string SceneManager::Resolve(std::string sceneId) { return sceneId; }");
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeSceneAssetReferenceResolver.cpp"), "#include \"NativeOwnership.hpp\"\nhe_cpp_make_scope_exit");
            File.WriteAllText(Path.Combine(OutputFolder, "RenderManager2D.cpp"), "#include \"NativeOwnership.hpp\"\ndelete font;");
            File.WriteAllText(Path.Combine(OutputFolder, "FontAsset.cpp"), "#include \"Widget.hpp\"");

            CPPGeneratedOwnershipValidator.Validate(OutputFolder);
        }

        /// <summary>
        /// Verifies non-engine font-asset fixtures do not require transient source-texture ownership cleanup markers.
        /// </summary>
        [Fact]
        public void Validate_whenFontAssetFixtureDoesNotMaterializeSourceTextureAsset_doesNotRequireCleanupMarkers() {
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeComponentRegistry.cpp"), "#include \"GeneratedRuntimeComponentDeserializerRegistration.hpp\"\nRegisterGeneratedRuntimeComponentDeserializers(registry);");
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeComponentRegistry.hpp"), "class RuntimeComponentRegistry { };");
            File.WriteAllText(Path.Combine(OutputFolder, "SceneManager.cpp"), "delete operation; delete loadResult; delete loadedSceneRecord; he_cpp_make_scope_exit");
            File.WriteAllText(Path.Combine(OutputFolder, "RuntimeSceneAssetReferenceResolver.cpp"), "#include \"NativeOwnership.hpp\"\nhe_cpp_make_scope_exit");
            File.WriteAllText(Path.Combine(OutputFolder, "RenderManager2D.cpp"), "#include \"NativeOwnership.hpp\"\ndelete font;");
            File.WriteAllText(Path.Combine(OutputFolder, "FontAsset.cpp"), "int32_t FontAsset::Height() { return 12; }");

            CPPGeneratedOwnershipValidator.Validate(OutputFolder);
        }
    }
}
