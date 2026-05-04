namespace codegen;

/// <summary>
/// Stores the normalized command-line values consumed by the codegen executable.
/// </summary>
public sealed class CodegenCliParsedArguments {
    /// <summary>
    /// Gets or sets the absolute path to the source project that should be converted.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the output directory that receives generated files.
    /// </summary>
    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional runtime-root override used to locate native support assets.
    /// </summary>
    public string RuntimeRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target platform identifier requested by the caller.
    /// </summary>
    public string PlatformId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the emitted output language identifier.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected output endianness token.
    /// </summary>
    public string Endianness { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional compiler profile override.
    /// </summary>
    public string CompilerProfileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional runtime profile override.
    /// </summary>
    public string RuntimeProfileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional named conversion preset selected for the run.
    /// </summary>
    public string PresetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the selected free-form option assignments keyed by setting id.
    /// </summary>
    public Dictionary<string, string> SelectedOptions { get; } = new(StringComparer.OrdinalIgnoreCase);
}
