namespace cs2.cpp;

/// <summary>
/// Emits generated runtime component deserializer registration support files when the generated runtime component registry expects the native free-function bridge.
/// </summary>
public static class CPPGeneratedRuntimeComponentRegistrationSupportWriter {
    const string RegistrationHeaderFileName = "GeneratedRuntimeComponentDeserializerRegistration.hpp";
    const string RegistrationSourceFileName = "GeneratedRuntimeComponentDeserializerRegistration.cpp";

    /// <summary>
    /// Writes the generated registration support files into one emitted C++ output tree when the runtime component registry references them.
    /// </summary>
    /// <param name="outputFolder">Generated C++ output folder to inspect and update.</param>
    /// <returns>Written file paths in deterministic order. Returns an empty list when the support files are not required.</returns>
    public static IReadOnlyList<string> WriteIfRequired(string outputFolder) {
        if (string.IsNullOrWhiteSpace(outputFolder)) {
            throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
        }

        string runtimeComponentRegistrySourcePath = Path.Combine(outputFolder, "RuntimeComponentRegistry.cpp");
        string runtimeComponentRegistryHeaderPath = Path.Combine(outputFolder, "RuntimeComponentRegistry.hpp");
        if (!File.Exists(runtimeComponentRegistrySourcePath) || !File.Exists(runtimeComponentRegistryHeaderPath)) {
            return Array.Empty<string>();
        }

        string runtimeComponentRegistrySource = File.ReadAllText(runtimeComponentRegistrySourcePath);
        string runtimeComponentRegistryHeader = File.ReadAllText(runtimeComponentRegistryHeaderPath);
        if (!RequiresGeneratedRuntimeComponentDeserializerRegistration(runtimeComponentRegistrySource, runtimeComponentRegistryHeader)) {
            return Array.Empty<string>();
        }

        string registrationHeaderPath = Path.Combine(outputFolder, RegistrationHeaderFileName);
        string registrationSourcePath = Path.Combine(outputFolder, RegistrationSourceFileName);
        File.WriteAllText(registrationHeaderPath, BuildRegistrationHeaderText());
        File.WriteAllText(registrationSourcePath, BuildRegistrationSourceText());
        return [
            registrationHeaderPath,
            registrationSourcePath
        ];
    }

    /// <summary>
    /// Returns whether the generated runtime component registry expects the native generated registration bridge.
    /// </summary>
    /// <param name="source">Generated runtime component registry source.</param>
    /// <param name="header">Generated runtime component registry header.</param>
    /// <returns><c>true</c> when the generated support files are required; otherwise <c>false</c>.</returns>
    static bool RequiresGeneratedRuntimeComponentDeserializerRegistration(string source, string header) {
        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(header)) {
            return false;
        }

        return (source?.Contains("RegisterGeneratedRuntimeComponentDeserializers(", StringComparison.Ordinal) ?? false)
            || (header?.Contains("RegisterGeneratedRuntimeComponentDeserializers(", StringComparison.Ordinal) ?? false);
    }

    /// <summary>
    /// Builds the generated registration header consumed by the runtime component registry free-function hook.
    /// </summary>
    /// <returns>Header text for one generated registration declaration.</returns>
    static string BuildRegistrationHeaderText() {
        return "#pragma once" + Environment.NewLine
            + "#ifdef DrawText" + Environment.NewLine
            + "#undef DrawText" + Environment.NewLine
            + "#endif" + Environment.NewLine
            + "class RuntimeComponentRegistry;" + Environment.NewLine + Environment.NewLine
            + "void RegisterGeneratedRuntimeComponentDeserializers(::RuntimeComponentRegistry* registry);" + Environment.NewLine;
    }

    /// <summary>
    /// Builds the generated registration source consumed by the runtime component registry free-function hook.
    /// </summary>
    /// <returns>Source text for one generated registration implementation.</returns>
    static string BuildRegistrationSourceText() {
        return "#ifdef DrawText" + Environment.NewLine
            + "#undef DrawText" + Environment.NewLine
            + "#endif" + Environment.NewLine
            + "#include \"GeneratedRuntimeComponentDeserializerRegistration.hpp\"" + Environment.NewLine
            + "#include \"RuntimeComponentRegistry.hpp\"" + Environment.NewLine
            + "#include \"runtime/native_exceptions.hpp\"" + Environment.NewLine
            + Environment.NewLine
            + "void RegisterGeneratedRuntimeComponentDeserializers(::RuntimeComponentRegistry* registry)" + Environment.NewLine
            + "{" + Environment.NewLine
            + "    if (registry == nullptr)" + Environment.NewLine
            + "    {" + Environment.NewLine
            + "        throw new ArgumentNullException(\"registry\");" + Environment.NewLine
            + "    }" + Environment.NewLine
            + "}" + Environment.NewLine;
    }
}
