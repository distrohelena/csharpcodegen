using cs2.cpp;

namespace codegen;

/// <summary>
/// Builds C++ conversion options from parsed codegen command-line arguments.
/// </summary>
public static class CodegenCliOptionsBuilder {
    /// <summary>
    /// Creates conversion options for one parsed CLI invocation.
    /// </summary>
    /// <param name="parsedArguments">Normalized parsed arguments.</param>
    /// <returns>Conversion options ready for the C++ converter.</returns>
    public static CPPConversionOptions CreateConversionOptions(CodegenCliParsedArguments parsedArguments) {
        if (parsedArguments == null) {
            throw new ArgumentNullException(nameof(parsedArguments));
        }

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.PresetId = parsedArguments.PresetId ?? string.Empty;
        options.CompilerProfile = CreateCompilerProfile(parsedArguments.PlatformId, parsedArguments.CompilerProfileName);
        options.PlatformProfile = CreatePlatformProfile(parsedArguments.PlatformId, parsedArguments.Endianness);
        options.RuntimeProfile = CreateRuntimeProfile(parsedArguments.RuntimeProfileName);
        options.BuildFeatureProfile = CPPBuildFeatureProfile.CreateDefault();
        options.CollectDiagnostics = true;
        options.FailOnError = true;
        options.IncludeProjectDefinedPreprocessorSymbols = true;
        options.LoadNativeRuntimeMetadata = true;
        options.WriteConversionReport = true;
        options.WindowsHandoffOutputFolder = parsedArguments.OutputFolder;

        ApplyBooleanOption(parsedArguments.SelectedOptions, "write-conversion-report", value => options.WriteConversionReport = value);
        ApplyBooleanOption(parsedArguments.SelectedOptions, "include-project-defined-preprocessor-symbols", value => options.IncludeProjectDefinedPreprocessorSymbols = value);
        ApplyBooleanOption(parsedArguments.SelectedOptions, "load-native-runtime-metadata", value => options.LoadNativeRuntimeMetadata = value);
        ApplyBooleanOption(parsedArguments.SelectedOptions, "fail-on-error", value => options.FailOnError = value);
        ApplyBooleanOption(parsedArguments.SelectedOptions, "collect-diagnostics", value => options.CollectDiagnostics = value);

        if (TryGetStringOption(parsedArguments.SelectedOptions, "windows-handoff-output-folder", out string handoffOutputFolder)) {
            options.WindowsHandoffOutputFolder = handoffOutputFolder;
        }
        if (TryGetStringOption(parsedArguments.SelectedOptions, "additional-preprocessor-symbols", out string additionalSymbols)) {
            options.AdditionalPreprocessorSymbols = additionalSymbols
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return options;
    }

    /// <summary>
    /// Resolves the compiler profile requested by one CLI invocation.
    /// </summary>
    /// <param name="platformId">Target platform identifier.</param>
    /// <param name="compilerProfileName">Optional explicit compiler profile override.</param>
    /// <returns>The resolved compiler profile.</returns>
    static CPPCompilerProfile CreateCompilerProfile(string platformId, string compilerProfileName) {
        if (!string.IsNullOrWhiteSpace(compilerProfileName)) {
            if (string.Equals(compilerProfileName, "msvc", StringComparison.OrdinalIgnoreCase)) {
                return CPPCompilerProfile.CreateMsvc();
            } else if (string.Equals(compilerProfileName, "gcc", StringComparison.OrdinalIgnoreCase)) {
                return CPPCompilerProfile.CreateGcc();
            }

            throw new NotSupportedException($"Unsupported compiler profile '{compilerProfileName}'.");
        }

        return string.Equals(platformId, "windows", StringComparison.OrdinalIgnoreCase)
            ? CPPCompilerProfile.CreateMsvc()
            : CPPCompilerProfile.CreateGcc();
    }

    /// <summary>
    /// Resolves the platform profile requested by one CLI invocation.
    /// </summary>
    /// <param name="platformId">Target platform identifier.</param>
    /// <param name="endianness">Optional endianness override token.</param>
    /// <returns>The resolved platform profile.</returns>
    static CPPPlatformProfile CreatePlatformProfile(string platformId, string endianness) {
        CPPPlatformProfile platformProfile;
        if (string.Equals(platformId, "gamecube", StringComparison.OrdinalIgnoreCase)) {
            platformProfile = CPPPlatformProfile.CreateGameCubeHeadless();
        } else if (string.Equals(platformId, "ps2", StringComparison.OrdinalIgnoreCase)) {
            platformProfile = CPPPlatformProfile.CreatePlayStation2Headless();
        } else if (string.Equals(platformId, "n64", StringComparison.OrdinalIgnoreCase)) {
            platformProfile = CPPPlatformProfile.CreateNintendo64Headless();
        } else {
            platformProfile = CPPPlatformProfile.CreateWindowsHeadless();
        }

        bool isLittleEndian = !string.Equals(endianness, "big", StringComparison.OrdinalIgnoreCase);
        platformProfile.IsLittleEndian = isLittleEndian;
        platformProfile.IsWindowsHost = string.Equals(platformId, "windows", StringComparison.OrdinalIgnoreCase);
        platformProfile.Name = $"{platformId}-headless";
        platformProfile.DefineName = $"HE_CPP_PLATFORM_{platformId.ToUpperInvariant()}";
        return platformProfile;
    }

    /// <summary>
    /// Resolves the runtime profile requested by one CLI invocation.
    /// </summary>
    /// <param name="runtimeProfileName">Optional explicit runtime profile override.</param>
    /// <returns>The resolved runtime profile.</returns>
    static CPPRuntimeProfile CreateRuntimeProfile(string runtimeProfileName) {
        if (string.IsNullOrWhiteSpace(runtimeProfileName) || string.Equals(runtimeProfileName, "stl-lite", StringComparison.OrdinalIgnoreCase)) {
            return CPPRuntimeProfile.CreateStlLite();
        } else if (string.Equals(runtimeProfileName, "custom-retro", StringComparison.OrdinalIgnoreCase)) {
            return CPPRuntimeProfile.CreateCustomRetro();
        }

        throw new NotSupportedException($"Unsupported runtime profile '{runtimeProfileName}'.");
    }

    /// <summary>
    /// Applies one optional boolean assignment from the selected-options map.
    /// </summary>
    /// <param name="values">Selected-option assignments keyed by setting id.</param>
    /// <param name="key">Option key to read.</param>
    /// <param name="setter">Mutation action that receives the parsed boolean value.</param>
    static void ApplyBooleanOption(IReadOnlyDictionary<string, string> values, string key, Action<bool> setter) {
        if (!values.ContainsKey(key)) {
            return;
        }

        string rawValue = values[key] ?? string.Empty;
        if (bool.TryParse(rawValue, out bool parsedValue)) {
            setter(parsedValue);
        }
    }

    /// <summary>
    /// Reads one optional string assignment from the selected-options map.
    /// </summary>
    /// <param name="values">Selected-option assignments keyed by setting id.</param>
    /// <param name="key">Option key to read.</param>
    /// <param name="value">Resolved option value when present.</param>
    /// <returns>True when a non-empty value was found; otherwise false.</returns>
    static bool TryGetStringOption(IReadOnlyDictionary<string, string> values, string key, out string value) {
        value = string.Empty;
        if (!values.ContainsKey(key)) {
            return false;
        }

        string rawValue = values[key] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return false;
        }

        value = rawValue;
        return true;
    }
}
