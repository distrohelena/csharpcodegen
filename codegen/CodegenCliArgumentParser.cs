namespace codegen;

/// <summary>
/// Parses the command-line arguments accepted by the C# to C++ codegen executable.
/// </summary>
public static class CodegenCliArgumentParser {
    /// <summary>
    /// Attempts to parse one command line into normalized codegen arguments.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="parsedArguments">Normalized parsed arguments when parsing succeeds.</param>
    /// <param name="errorMessage">Human-readable parse failure message when parsing fails.</param>
    /// <returns>True when parsing succeeds; otherwise false.</returns>
    public static bool TryParseArguments(string[] args, out CodegenCliParsedArguments parsedArguments, out string errorMessage) {
        parsedArguments = new CodegenCliParsedArguments();
        errorMessage = string.Empty;

        try {
            if (args == null || args.Length == 0) {
                errorMessage = "Missing arguments.";
                return false;
            }

            int index = 0;
            if (string.Equals(args[index], "--cpp", StringComparison.OrdinalIgnoreCase)) {
                index++;
            } else {
                errorMessage = "The first argument must be '--cpp'.";
                return false;
            }

            if (index == args.Length) {
                errorMessage = "Missing project and output arguments.";
                return false;
            }

            if (index + 1 == args.Length && !args[index].StartsWith("--", StringComparison.Ordinal)) {
                errorMessage = "The legacy positional form requires both project and output arguments.";
                return false;
            }

            if (index + 1 < args.Length
                && !args[index].StartsWith("--", StringComparison.Ordinal)
                && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                && args.Length == index + 2) {
                parsedArguments.ProjectPath = Path.GetFullPath(args[index]);
                parsedArguments.OutputFolder = Path.GetFullPath(args[index + 1]);
                parsedArguments.Language = "cpp";
                parsedArguments.PlatformId = "windows";
                parsedArguments.Endianness = "little";
                return true;
            }

            while (index < args.Length) {
                string token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal)) {
                    errorMessage = $"Unexpected positional argument '{token}'.";
                    return false;
                }

                string optionName = token[2..];
                switch (optionName) {
                    case "project":
                        parsedArguments.ProjectPath = RequireValue(args, ref index, optionName);
                        break;
                    case "output":
                        parsedArguments.OutputFolder = RequireValue(args, ref index, optionName);
                        break;
                    case "runtime-root":
                        parsedArguments.RuntimeRoot = RequireValue(args, ref index, optionName);
                        break;
                    case "platform":
                        parsedArguments.PlatformId = RequireValue(args, ref index, optionName);
                        break;
                    case "language":
                        parsedArguments.Language = RequireValue(args, ref index, optionName);
                        break;
                    case "endianness":
                        parsedArguments.Endianness = RequireValue(args, ref index, optionName);
                        break;
                    case "compiler":
                        parsedArguments.CompilerProfileName = RequireValue(args, ref index, optionName);
                        break;
                    case "runtime":
                        parsedArguments.RuntimeProfileName = RequireValue(args, ref index, optionName);
                        break;
                    case "preset":
                        parsedArguments.PresetId = RequireValue(args, ref index, optionName);
                        break;
                    case "feature-catalog":
                        parsedArguments.FeatureCatalogPath = RequireValue(args, ref index, optionName);
                        break;
                    case "set":
                        AddSelectedOption(args, ref index, parsedArguments.SelectedOptions);
                        break;
                    default:
                        errorMessage = $"Unknown argument '{token}'.";
                        return false;
                }

                index++;
            }

            if (string.IsNullOrWhiteSpace(parsedArguments.ProjectPath)) {
                errorMessage = "The '--project' argument is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(parsedArguments.OutputFolder)) {
                errorMessage = "The '--output' argument is required.";
                return false;
            }

            parsedArguments.ProjectPath = Path.GetFullPath(parsedArguments.ProjectPath);
            parsedArguments.OutputFolder = Path.GetFullPath(parsedArguments.OutputFolder);
            if (!string.IsNullOrWhiteSpace(parsedArguments.RuntimeRoot)) {
                parsedArguments.RuntimeRoot = Path.GetFullPath(parsedArguments.RuntimeRoot);
            }
            if (!string.IsNullOrWhiteSpace(parsedArguments.FeatureCatalogPath)) {
                parsedArguments.FeatureCatalogPath = Path.GetFullPath(parsedArguments.FeatureCatalogPath);
            }
            if (string.IsNullOrWhiteSpace(parsedArguments.PlatformId)) {
                parsedArguments.PlatformId = "windows";
            }
            if (string.IsNullOrWhiteSpace(parsedArguments.Language)) {
                parsedArguments.Language = "cpp";
            }
            if (string.IsNullOrWhiteSpace(parsedArguments.Endianness)) {
                parsedArguments.Endianness = "little";
            }

            return true;
        } catch (Exception ex) {
            errorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Reads the next token as the value for one switch.
    /// </summary>
    /// <param name="args">Full command-line argument array.</param>
    /// <param name="index">Current switch index that will advance to the value token.</param>
    /// <param name="optionName">Switch name used in validation errors.</param>
    /// <returns>The parsed option value.</returns>
    static string RequireValue(string[] args, ref int index, string optionName) {
        if (index + 1 >= args.Length) {
            throw new ArgumentException($"Option '--{optionName}' requires a value.");
        }

        index++;
        return args[index];
    }

    /// <summary>
    /// Parses one `--set key=value` assignment into the selected-options map.
    /// </summary>
    /// <param name="args">Full command-line argument array.</param>
    /// <param name="index">Current switch index that will advance to the assignment token.</param>
    /// <param name="selectedOptions">Mutable selected-options map that receives the parsed assignment.</param>
    static void AddSelectedOption(string[] args, ref int index, Dictionary<string, string> selectedOptions) {
        string assignment = RequireValue(args, ref index, "set");
        int equalsIndex = assignment.IndexOf('=');
        if (equalsIndex <= 0 || equalsIndex == assignment.Length - 1) {
            throw new ArgumentException("The '--set' option expects 'key=value'.");
        }

        string key = assignment[..equalsIndex].Trim();
        string value = assignment[(equalsIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("The '--set' option expects a non-empty key.");
        }

        selectedOptions[key] = value;
    }
}
