using cs2.cpp;

namespace codegen;

/// <summary>
/// Entry point for the C# to C++ command-line conversion executable.
/// </summary>
internal static class Program {
    /// <summary>
    /// Exit code returned when conversion reaches an unexpected failure after argument validation.
    /// </summary>
    const int ConversionFailureExitCode = 2;

    /// <summary>
    /// Starts one CLI conversion request and returns its process exit code.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns>Process exit code.</returns>
    static int Main(string[] args) {
        return Execute(args);
    }

    /// <summary>
    /// Executes one CLI conversion request.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns>Process exit code.</returns>
    internal static int Execute(string[] args) {
        try {
            if (args.Length > 0 && args[0] == "--write-feature-manifest") {
                return WriteFeatureManifest(args);
            }
            if (!CodegenCliArgumentParser.TryParseArguments(args, out CodegenCliParsedArguments parsedArguments, out string errorMessage)) {
                Console.Error.WriteLine(errorMessage);
                Console.Error.WriteLine("Usage: --cpp --project <project.csproj> --output <output-folder> [--runtime-root <folder>] [--platform <platform-id>] [--runtime stl-lite|custom-retro|freestanding] [--language cpp] [--endianness little|big] [--preset <preset-id>] [--set key=value ...]");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(parsedArguments.RuntimeRoot)) {
                Environment.SetEnvironmentVariable("CS2_RUNTIME_ROOT", Path.GetFullPath(parsedArguments.RuntimeRoot));
            }
            if (!File.Exists(parsedArguments.ProjectPath)) {
                Console.Error.WriteLine($"The .csproj file '{parsedArguments.ProjectPath}' does not exist.");
                return 1;
            }
            if (!string.Equals(parsedArguments.Language, "cpp", StringComparison.OrdinalIgnoreCase)) {
                Console.Error.WriteLine($"Unsupported output language '{parsedArguments.Language}'. This CLI currently supports only C++ output.");
                return 1;
            }

            CPPConversionOptions options = CodegenCliOptionsBuilder.CreateConversionOptions(parsedArguments);
            CPPCodeConverter converter = new(new CPPConversionRules(), options);
            typeof(CPPCodeConverter).BaseType!
                .GetMethod("AddCsproj", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!
                .Invoke(converter, [parsedArguments.ProjectPath]);
            converter.WriteOutput(parsedArguments.OutputFolder);

            Console.WriteLine("C++ conversion completed.");
            return 0;
        } catch (Exception exception) {
            Exception diagnosticException = UnwrapInvocationException(exception);
            Console.Error.WriteLine($"Codegen failed: {diagnosticException.Message}");
            return ConversionFailureExitCode;
        }
    }

    /// <summary>Runs the manifest-only producer without loading managed projects, clearing generated trees or converting any class.</summary>
    /// <param name="args">Manifest-only command and mandatory report, catalog and output paths.</param>
    /// <returns>Zero after the generated manifest pair and feature config have been written successfully.</returns>
    static int WriteFeatureManifest(string[] args) {
        if (args.Length != 7 || args[1] != "--report" || args[3] != "--feature-catalog" || args[5] != "--output") {
            throw new ArgumentException("Usage: --write-feature-manifest --report <combined-report.json> --feature-catalog <catalog.json> --output <combined-source-folder>");
        }
        CPPFeatureManifestWriter.WriteFromConversionReport(args[2], args[6], args[4]);
        Console.WriteLine("Combined feature manifest written.");
        return 0;
    }

    /// <summary>
    /// Removes reflection invocation wrappers while retaining the converter's original diagnostic exception.
    /// </summary>
    /// <param name="exception">Exception raised while executing the CLI request.</param>
    /// <returns>The innermost exception that carries the useful conversion diagnostic.</returns>
    static Exception UnwrapInvocationException(Exception exception) {
        Exception diagnosticException = exception;
        while (diagnosticException is System.Reflection.TargetInvocationException invocationException
            && invocationException.InnerException != null) {
            diagnosticException = invocationException.InnerException;
        }

        return diagnosticException;
    }
}
