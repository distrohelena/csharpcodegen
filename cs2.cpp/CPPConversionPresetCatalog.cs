namespace cs2.cpp {
    /// <summary>
    /// Resolves named conversion presets into concrete compiler, platform, runtime, feature, and restriction profiles.
    /// </summary>
    public class CPPConversionPresetCatalog {
        /// <summary>
        /// Applies a named preset to mutable conversion options.
        /// </summary>
        /// <param name="options">Conversion options that should receive the resolved preset profiles.</param>
        /// <returns>The same options instance after preset resolution.</returns>
        public CPPConversionOptions ApplyTo(CPPConversionOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.PresetId)) {
                return options;
            }

            CPPConversionPreset preset = Resolve(options.PresetId);
            options.PresetId = preset.Id;
            options.CompilerProfile = preset.CompilerProfile;
            options.PlatformProfile = preset.PlatformProfile;
            options.RuntimeProfile = preset.RuntimeProfile;
            options.BuildFeatureProfile = preset.BuildFeatureProfile;
            options.RestrictionProfile = preset.RestrictionProfile;
            options.AdditionalPreprocessorSymbols = preset.AdditionalPreprocessorSymbols;
            options.IncludeProjectDefinedPreprocessorSymbols = preset.IncludeProjectDefinedPreprocessorSymbols;
            return options;
        }

        /// <summary>
        /// Resolves a named conversion preset.
        /// </summary>
        /// <param name="presetId">Stable preset identifier to resolve.</param>
        /// <returns>The resolved conversion preset.</returns>
        public CPPConversionPreset Resolve(string presetId) {
            if (string.IsNullOrWhiteSpace(presetId)) {
                throw new ArgumentException("Preset id must not be empty.", nameof(presetId));
            }

            if (string.Equals(presetId, "windows-shaders", StringComparison.OrdinalIgnoreCase)) {
                return CreateWindowsShadersPreset();
            }

            if (string.Equals(presetId, "windows-no-shaders", StringComparison.OrdinalIgnoreCase)) {
                return CreateWindowsNoShadersPreset();
            }

            if (string.Equals(presetId, "ps2-lite", StringComparison.OrdinalIgnoreCase)) {
                return CreatePlayStation2LitePreset();
            }

            if (string.Equals(presetId, "gamecube-core-boot", StringComparison.OrdinalIgnoreCase)) {
                return CreateGameCubeCoreBootPreset();
            }

            if (string.Equals(presetId, "n64-minimal", StringComparison.OrdinalIgnoreCase)) {
                return CreateNintendo64MinimalPreset();
            }

            throw new InvalidOperationException($"Unknown C++ conversion preset '{presetId}'.");
        }

        /// <summary>
        /// Creates the permissive Windows preset with shader support enabled.
        /// </summary>
        /// <returns>The resolved Windows shader-capable preset.</returns>
        static CPPConversionPreset CreateWindowsShadersPreset() {
            return new CPPConversionPreset {
                Id = "windows-shaders",
                CompilerProfile = CPPCompilerProfile.CreateMsvc(),
                PlatformProfile = CPPPlatformProfile.CreateWindowsHeadless(),
                RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
                BuildFeatureProfile = CPPBuildFeatureProfile.CreateDefault(),
                RestrictionProfile = CPPRestrictionProfile.CreatePermissive("desktop"),
                IncludeProjectDefinedPreprocessorSymbols = true,
                AdditionalPreprocessorSymbols = Array.Empty<string>()
            };
        }

        /// <summary>
        /// Creates the Windows preset that disables shader systems while leaving the rest of the desktop runtime permissive.
        /// </summary>
        /// <returns>The resolved Windows shader-disabled preset.</returns>
        static CPPConversionPreset CreateWindowsNoShadersPreset() {
            CPPBuildFeatureProfile featureProfile = CPPBuildFeatureProfile.CreateDefault()
                .WithMode("shaders", CPPFeatureMode.Disabled);

            return new CPPConversionPreset {
                Id = "windows-no-shaders",
                CompilerProfile = CPPCompilerProfile.CreateMsvc(),
                PlatformProfile = CPPPlatformProfile.CreateWindowsHeadless(),
                RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
                BuildFeatureProfile = featureProfile,
                RestrictionProfile = new CPPRestrictionProfile {
                    Name = "desktop-no-shaders",
                    ForbidShaders = true
                },
                IncludeProjectDefinedPreprocessorSymbols = true,
                AdditionalPreprocessorSymbols = Array.Empty<string>()
            };
        }

        /// <summary>
        /// Creates the low-footprint PlayStation 2 preset.
        /// </summary>
        /// <returns>The resolved PlayStation 2 preset.</returns>
        static CPPConversionPreset CreatePlayStation2LitePreset() {
            CPPBuildFeatureProfile featureProfile = CPPBuildFeatureProfile.CreateDefault()
                .WithMode("shaders", CPPFeatureMode.Disabled)
                .WithMode("debug_overlay", CPPFeatureMode.Disabled);

            return new CPPConversionPreset {
                Id = "ps2-lite",
                CompilerProfile = CPPCompilerProfile.CreateGcc(),
                PlatformProfile = CPPPlatformProfile.CreatePlayStation2Headless(),
                RuntimeProfile = CPPRuntimeProfile.CreateCustomRetro(),
                BuildFeatureProfile = featureProfile,
                RestrictionProfile = new CPPRestrictionProfile {
                    Name = "ps2-lite",
                    ForbidShaders = true,
                    ForbidRuntimeJson = true,
                    ForbidReflectionLikeRuntime = true,
                    ForbidRegex = true,
                    ForbidDebugOnlySystems = true
                },
                IncludeProjectDefinedPreprocessorSymbols = false,
                AdditionalPreprocessorSymbols = new[] {
                    "HELENGINE_RUNTIME_MATERIAL_RESOLUTION_COOKED_PLATFORM_OWNED",
                    "HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION",
                    "HELENGINE_CODEGEN_DISABLE_MENU_REFLECTION"
                }
            };
        }

        /// <summary>
        /// Creates the first GameCube preset used for generated-core boot validation.
        /// </summary>
        /// <returns>The resolved GameCube core-boot preset.</returns>
        static CPPConversionPreset CreateGameCubeCoreBootPreset() {
            CPPBuildFeatureProfile featureProfile = CPPBuildFeatureProfile.CreateDefault()
                .WithMode("shaders", CPPFeatureMode.Disabled)
                .WithMode("debug_overlay", CPPFeatureMode.Disabled);

            return new CPPConversionPreset {
                Id = "gamecube-core-boot",
                CompilerProfile = CPPCompilerProfile.CreateGcc(),
                PlatformProfile = CPPPlatformProfile.CreateGameCubeHeadless(),
                RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
                BuildFeatureProfile = featureProfile,
                RestrictionProfile = new CPPRestrictionProfile {
                    Name = "gamecube-core-boot",
                    ForbidShaders = true,
                    ForbidRuntimeJson = true,
                    ForbidReflectionLikeRuntime = true,
                    ForbidDebugOnlySystems = true
                },
                IncludeProjectDefinedPreprocessorSymbols = false,
                AdditionalPreprocessorSymbols = new[] {
                    "HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION",
                    "HELENGINE_CODEGEN_DISABLE_MENU_REFLECTION"
                }
            };
        }

        /// <summary>
        /// Creates the minimal Nintendo 64 preset.
        /// </summary>
        /// <returns>The resolved Nintendo 64 preset.</returns>
        static CPPConversionPreset CreateNintendo64MinimalPreset() {
            CPPBuildFeatureProfile featureProfile = CPPBuildFeatureProfile.CreateDefault()
                .WithMode("shaders", CPPFeatureMode.Disabled)
                .WithMode("debug_overlay", CPPFeatureMode.Disabled)
                .WithMode("render2d", CPPFeatureMode.Disabled)
                .WithMode("text2d", CPPFeatureMode.Disabled);

            return new CPPConversionPreset {
                Id = "n64-minimal",
                CompilerProfile = CPPCompilerProfile.CreateGcc(),
                PlatformProfile = CPPPlatformProfile.CreateNintendo64Headless(),
                RuntimeProfile = CPPRuntimeProfile.CreateCustomRetro(),
                BuildFeatureProfile = featureProfile,
                RestrictionProfile = new CPPRestrictionProfile {
                    Name = "n64-minimal",
                    ForbidShaders = true,
                    ForbidRuntimeJson = true,
                    ForbidReflectionLikeRuntime = true,
                    ForbidRegex = true,
                    ForbidDebugOnlySystems = true
                },
                IncludeProjectDefinedPreprocessorSymbols = false,
                AdditionalPreprocessorSymbols = new[] {
                    "HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION",
                    "HELENGINE_CODEGEN_DISABLE_MENU_REFLECTION"
                }
            };
        }
    }
}
