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
            options.PlatformOptionValues = MergePlatformOptionValues(
                preset.PlatformOptionValues,
                options.PlatformOptionValues);
            options.BuildFeatureProfile = CPPFeatureProfileOptionResolver.BuildProfile(
                options.PlatformOptionValues,
                options.FeatureCatalog);
            options.RestrictionProfile = preset.RestrictionProfile;
            options.AdditionalPreprocessorSymbols = MergeAdditionalPreprocessorSymbols(
                options.AdditionalPreprocessorSymbols,
                preset.AdditionalPreprocessorSymbols);
            options.IncludeProjectDefinedPreprocessorSymbols = preset.IncludeProjectDefinedPreprocessorSymbols;
            return options;
        }

        /// <summary>
        /// Merges preset-owned option defaults with caller-selected option overrides.
        /// </summary>
        /// <param name="presetValues">Generic option defaults owned by the preset.</param>
        /// <param name="callerValues">Caller-selected generic option values.</param>
        /// <returns>Merged generic option map with caller values taking precedence.</returns>
        static IReadOnlyDictionary<string, string> MergePlatformOptionValues(
            IReadOnlyDictionary<string, string> presetValues,
            IReadOnlyDictionary<string, string> callerValues) {
            Dictionary<string, string> mergedValues = new(StringComparer.OrdinalIgnoreCase);
            AppendOptionValues(mergedValues, presetValues);
            AppendOptionValues(mergedValues, callerValues);
            return mergedValues;
        }

        /// <summary>
        /// Merges caller-provided preprocessor symbols with preset-owned symbols while preserving the caller ordering.
        /// </summary>
        /// <param name="callerSymbols">Symbols already supplied by the caller before preset application.</param>
        /// <param name="presetSymbols">Symbols owned by the resolved preset.</param>
        /// <returns>Ordered unique symbol list containing both caller and preset entries.</returns>
        static IReadOnlyList<string> MergeAdditionalPreprocessorSymbols(
            IReadOnlyList<string> callerSymbols,
            IReadOnlyList<string> presetSymbols) {
            HashSet<string> seenSymbols = new(StringComparer.Ordinal);
            List<string> mergedSymbols = new();
            AppendUniqueSymbols(mergedSymbols, seenSymbols, callerSymbols);
            AppendUniqueSymbols(mergedSymbols, seenSymbols, presetSymbols);
            return mergedSymbols;
        }

        /// <summary>
        /// Appends non-empty preprocessor symbols that have not already been added to the merged preset symbol list.
        /// </summary>
        /// <param name="destination">Ordered merged symbol list.</param>
        /// <param name="seenSymbols">Set tracking symbols already appended.</param>
        /// <param name="symbols">Candidate symbols to append.</param>
        static void AppendUniqueSymbols(
            List<string> destination,
            HashSet<string> seenSymbols,
            IReadOnlyList<string> symbols) {
            if (destination == null) {
                throw new ArgumentNullException(nameof(destination));
            }
            if (seenSymbols == null) {
                throw new ArgumentNullException(nameof(seenSymbols));
            }
            if (symbols == null) {
                return;
            }

            for (int index = 0; index < symbols.Count; index++) {
                string symbol = symbols[index];
                if (string.IsNullOrWhiteSpace(symbol) || !seenSymbols.Add(symbol)) {
                    continue;
                }

                destination.Add(symbol);
            }
        }

        /// <summary>
        /// Appends non-empty generic option values into the merged option map.
        /// </summary>
        /// <param name="destination">Merged option map receiving the values.</param>
        /// <param name="values">Option map to append.</param>
        static void AppendOptionValues(
            Dictionary<string, string> destination,
            IReadOnlyDictionary<string, string> values) {
            if (destination == null) {
                throw new ArgumentNullException(nameof(destination));
            }
            if (values == null) {
                return;
            }

            foreach (KeyValuePair<string, string> pair in values) {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) {
                    continue;
                }

                destination[pair.Key] = pair.Value;
            }
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

            if (string.Equals(presetId, "ds-lite", StringComparison.OrdinalIgnoreCase)) {
                return CreateNintendoDsLitePreset();
            }

            if (string.Equals(presetId, "native-core-boot", StringComparison.OrdinalIgnoreCase)) {
                return CreateNativeCoreBootPreset();
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
            return new CPPConversionPreset {
                Id = "windows-no-shaders",
                CompilerProfile = CPPCompilerProfile.CreateMsvc(),
                PlatformProfile = CPPPlatformProfile.CreateWindowsHeadless(),
                RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
                RestrictionProfile = new CPPRestrictionProfile {
                    Name = "desktop-no-shaders",
                    ForbidShaders = true
                },
                IncludeProjectDefinedPreprocessorSymbols = true,
                AdditionalPreprocessorSymbols = Array.Empty<string>(),
                PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    [CPPCodegenOptionNames.ForcedDisabledFeatures] = "shaders"
                }
            };
        }

        /// <summary>
        /// Creates the low-footprint PlayStation 2 preset.
        /// </summary>
        /// <returns>The resolved PlayStation 2 preset.</returns>
        static CPPConversionPreset CreatePlayStation2LitePreset() {
            return new CPPConversionPreset {
                Id = "ps2-lite",
                CompilerProfile = CPPCompilerProfile.CreateGcc(),
                PlatformProfile = CPPPlatformProfile.CreatePlayStation2Headless(),
                RuntimeProfile = CPPRuntimeProfile.CreateCustomRetro(),
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
                },
                PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    [CPPCodegenOptionNames.ForcedDisabledFeatures] = "shaders;debug_overlay"
                }
            };
        }

        /// <summary>
        /// Creates the low-footprint Nintendo DS preset.
        /// </summary>
        /// <returns>The resolved Nintendo DS preset.</returns>
        static CPPConversionPreset CreateNintendoDsLitePreset() {
            return new CPPConversionPreset {
                Id = "ds-lite",
                CompilerProfile = CPPCompilerProfile.CreateGcc(),
                PlatformProfile = CPPPlatformProfile.CreateNintendoDsHeadless(),
                RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
                RestrictionProfile = new CPPRestrictionProfile {
                    Name = "ds-lite",
                    ForbidDebugOnlySystems = true
                },
                IncludeProjectDefinedPreprocessorSymbols = false,
                AdditionalPreprocessorSymbols = Array.Empty<string>(),
                PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    [CPPCodegenOptionNames.ForcedDisabledFeatures] = "debug_overlay"
                }
            };
        }

        /// <summary>
        /// Creates the first stripped native preset used for generated-core boot validation.
        /// </summary>
        /// <returns>The resolved stripped native core-boot preset.</returns>
        static CPPConversionPreset CreateNativeCoreBootPreset() {
            return new CPPConversionPreset {
                Id = "native-core-boot",
                CompilerProfile = CPPCompilerProfile.CreateGcc(),
                PlatformProfile = CPPPlatformProfile.CreateCustomHeadless("retroppc", false, CPPGeneratedMathConventionKind.NativeColumnVector, 4),
                RuntimeProfile = CPPRuntimeProfile.CreateStlLite(),
                RestrictionProfile = new CPPRestrictionProfile {
                    Name = "native-core-boot",
                    ForbidShaders = true,
                    ForbidRuntimeJson = true,
                    ForbidReflectionLikeRuntime = true,
                    ForbidDebugOnlySystems = true
                },
                IncludeProjectDefinedPreprocessorSymbols = false,
                AdditionalPreprocessorSymbols = new[] {
                    "HELENGINE_CODEGEN_DISABLE_RUNTIME_SCRIPT_REFLECTION",
                    "HELENGINE_CODEGEN_DISABLE_MENU_REFLECTION"
                },
                PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    [CPPCodegenOptionNames.ForcedDisabledFeatures] = "shaders;debug_overlay"
                }
            };
        }

        /// <summary>
        /// Creates the minimal Nintendo 64 preset.
        /// </summary>
        /// <returns>The resolved Nintendo 64 preset.</returns>
        static CPPConversionPreset CreateNintendo64MinimalPreset() {
            return new CPPConversionPreset {
                Id = "n64-minimal",
                CompilerProfile = CPPCompilerProfile.CreateGcc(),
                PlatformProfile = CPPPlatformProfile.CreateNintendo64Headless(),
                RuntimeProfile = CPPRuntimeProfile.CreateCustomRetro(),
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
                },
                PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    [CPPCodegenOptionNames.ForcedDisabledFeatures] = "shaders;debug_overlay;render2d;text2d"
                }
            };
        }
    }
}
