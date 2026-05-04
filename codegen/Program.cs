using cs2.cpp;

namespace codegen;

/// <summary>
/// Entry point for the C# to C++ command-line conversion executable.
/// </summary>
internal static class Program {
    /// <summary>
    /// Executes one CLI conversion request.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <returns>Process exit code.</returns>
    static int Main(string[] args) {
        if (!CodegenCliArgumentParser.TryParseArguments(args, out CodegenCliParsedArguments parsedArguments, out string errorMessage)) {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine("Usage: --cpp --project <project.csproj> --output <output-folder> [--runtime-root <folder>] [--platform <platform-id>] [--language cpp] [--endianness little|big] [--preset <preset-id>] [--set key=value ...]");
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
    }
}
