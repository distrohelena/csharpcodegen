using System.Reflection;
using cs2.core.Pipeline;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the converter-level C++ pipeline wiring, run-state reset behavior, and generated sidecar outputs.
/// </summary>
public class CPPCodeConverterPipelineTests {
    /// <summary>
    /// Ensures the C++ backend uses its explicit stage ordering instead of the generic base pipeline.
    /// </summary>
    [Fact]
    public void BuildPipeline_UsesCppSpecificStageOrder() {
        TestableCPPCodeConverter converter = new TestableCPPCodeConverter(new CPPConversionRules(), CreateTestOptions());

        string[] stageNames = converter.GetPipelineStageNames();

        Assert.Equal([
            "CPPResetConversionStateStage",
            "ApplyPreprocessorSymbolsStage",
            "CPPPreprocessorFilterStage",
            "CPPAssemblyMetadataStage",
            "DocumentPreprocessingStage",
            "ClassProcessingStage",
            "ProgramSortingStage"
        ], stageNames);
    }

    /// <summary>
    /// Ensures converter output includes the generated config header and the serialized conversion report when enabled.
    /// </summary>
    [Fact]
    public void WriteOutput_WithReportEnabled_WritesConfigAndReportFiles() {
        CPPConversionOptions options = CreateTestOptions();
        options.WriteConversionReport = true;

        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));

        converter.WriteOutput(outputFolder);

        Assert.True(File.Exists(Path.Combine(outputFolder, CPPGeneratedConfigWriter.DefaultFileName)));
        Assert.True(File.Exists(Path.Combine(outputFolder, CPPConversionReportWriter.DefaultFileName)));
    }

    /// <summary>
    /// Ensures the generated output folder is fully cleared before a new emission pass so stale files cannot survive between builds.
    /// </summary>
    [Fact]
    public void WriteOutput_ClearsStaleFilesBeforeEmission() {
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), CreateTestOptions());
        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputFolder);
        File.WriteAllText(Path.Combine(outputFolder, "AssetContentProcessor.cpp"), "stale");
        File.WriteAllText(Path.Combine(outputFolder, "BinaryContentProcessor.cpp"), "stale");

        converter.WriteOutput(outputFolder);

        Assert.False(File.Exists(Path.Combine(outputFolder, "AssetContentProcessor.cpp")));
        Assert.False(File.Exists(Path.Combine(outputFolder, "BinaryContentProcessor.cpp")));
        Assert.True(File.Exists(Path.Combine(outputFolder, CPPGeneratedConfigWriter.DefaultFileName)));
    }

    /// <summary>
    /// Ensures per-run report state is cleared before a new conversion pipeline execution begins.
    /// </summary>
    [Fact]
    public void ResetRunState_ClearsPriorReportState() {
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), CreateTestOptions());
        converter.Report.AddDiagnostic(CPPDiagnosticSeverity.Error, "CPP999", "Stale error");
        converter.Report.ProcessedTypeCount = 4;
        converter.Report.EmittedFiles.Add("stale-file.hpp");

        converter.ResetRunState();

        Assert.Empty(converter.Report.Diagnostics);
        Assert.Equal(0, converter.Report.ProcessedTypeCount);
        Assert.Empty(converter.Report.EmittedFiles);
    }

    /// <summary>
    /// Exposes protected converter pipeline construction for focused tests.
    /// </summary>
    private sealed class TestableCPPCodeConverter : CPPCodeConverter {
        /// <summary>
        /// Initializes the test converter wrapper.
        /// </summary>
        /// <param name="rules">The conversion rules used by the backend.</param>
        /// <param name="options">The backend option set under test.</param>
        public TestableCPPCodeConverter(CPPConversionRules rules, CPPConversionOptions options)
            : base(rules, options) {
        }

        /// <summary>
        /// Gets the constructed pipeline stage names in registration order.
        /// </summary>
        /// <returns>The ordered stage type names.</returns>
        public string[] GetPipelineStageNames() {
            ConversionPipeline pipeline = BuildPipeline();
            FieldInfo field = typeof(ConversionPipeline).GetField("stages", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Unable to locate pipeline stage storage for test inspection.");
            IReadOnlyList<IConversionStage> stages = (IReadOnlyList<IConversionStage>)(field.GetValue(pipeline)
                ?? throw new InvalidOperationException("Conversion pipeline stage storage returned null."));
            return stages.Select(stage => stage.GetType().Name).ToArray();
        }
    }

    /// <summary>
    /// Creates focused converter options that avoid external runtime metadata tooling during unit tests.
    /// </summary>
    /// <returns>The option set used by the converter pipeline tests.</returns>
    static CPPConversionOptions CreateTestOptions() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        return options;
    }
}
