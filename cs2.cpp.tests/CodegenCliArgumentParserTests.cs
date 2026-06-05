using codegen;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the codegen CLI parser and options builder handle named conversion presets.
/// </summary>
public sealed class CodegenCliArgumentParserTests {
    /// <summary>
    /// Ensures the parser captures a selected preset id from the command line.
    /// </summary>
    [Fact]
    public void Try_parse_arguments_reads_named_preset_id() {
        string[] arguments = [
            "--cpp",
            "--project",
            @"C:\tmp\fixture.csproj",
            "--output",
            @"C:\tmp\generated",
            "--preset",
            "windows-no-shaders"
        ];

        bool success = CodegenCliArgumentParser.TryParseArguments(arguments, out CodegenCliParsedArguments parsedArguments, out string errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal("windows-no-shaders", parsedArguments.PresetId);
    }

    /// <summary>
    /// Ensures the options builder forwards the parsed preset id into the conversion options surface.
    /// </summary>
    [Fact]
    public void Create_conversion_options_sets_preset_id_from_parsed_arguments() {
        CodegenCliParsedArguments parsedArguments = new CodegenCliParsedArguments {
            ProjectPath = @"C:\tmp\fixture.csproj",
            OutputFolder = @"C:\tmp\generated",
            PlatformId = "windows",
            Language = "cpp",
            Endianness = "little",
            PresetId = "ps2-lite"
        };

        CPPConversionOptions options = CodegenCliOptionsBuilder.CreateConversionOptions(parsedArguments);

        Assert.Equal("ps2-lite", options.PresetId);
        Assert.Equal(@"C:\tmp\generated", options.WindowsHandoffOutputFolder);
    }

    /// <summary>
    /// Ensures caller-provided type remaps flow from generic `--set` arguments into the conversion options surface.
    /// </summary>
    [Fact]
    public void Create_conversion_options_reads_type_remaps_from_selected_options() {
        CodegenCliParsedArguments parsedArguments = new CodegenCliParsedArguments {
            ProjectPath = @"C:\tmp\fixture.csproj",
            OutputFolder = @"C:\tmp\generated",
            PlatformId = "windows",
            Language = "cpp",
            Endianness = "little"
        };
        parsedArguments.SelectedOptions["type-remaps"] = "System.Numerics.Vector3=helengine.float3|System.Numerics.Quaternion=helengine.float4";

        CPPConversionOptions options = CodegenCliOptionsBuilder.CreateConversionOptions(parsedArguments);

        Assert.Equal("helengine.float3", options.TypeRemaps["System.Numerics.Vector3"]);
        Assert.Equal("helengine.float4", options.TypeRemaps["System.Numerics.Quaternion"]);
    }

    /// <summary>
    /// Ensures the DS platform id resolves to a dedicated 32-bit platform profile instead of silently inheriting the Windows layout profile.
    /// </summary>
    [Fact]
    public void Create_conversion_options_for_ds_uses_nintendo_ds_platform_profile() {
        CodegenCliParsedArguments parsedArguments = new CodegenCliParsedArguments {
            ProjectPath = @"C:\tmp\fixture.csproj",
            OutputFolder = @"C:\tmp\generated",
            PlatformId = "ds",
            Language = "cpp",
            Endianness = "little"
        };

        CPPConversionOptions options = CodegenCliOptionsBuilder.CreateConversionOptions(parsedArguments);

        Assert.Equal(CPPPlatformKind.NintendoDsHeadless, options.PlatformProfile.Kind);
        Assert.Equal(4, options.PlatformProfile.PointerSizeInBytes);
        Assert.False(options.PlatformProfile.IsWindowsHost);
    }
}
