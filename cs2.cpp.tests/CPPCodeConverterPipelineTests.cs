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
            "CPPPInvokeAnalysisStage",
            "CPPOwnershipAnalysisStage",
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

        TestableCPPCodeConverter converter = new TestableCPPCodeConverter(new CPPConversionRules(), options);
        converter.UseEmptyPInvokePlan();
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
        TestableCPPCodeConverter converter = new TestableCPPCodeConverter(new CPPConversionRules(), CreateTestOptions());
        converter.UseEmptyPInvokePlan();
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
    /// Ensures regenerating one target replaces only its generated source, preserving other targets and native build state.
    /// </summary>
    [Fact]
    public void WriteOutput_WithDeploymentRoot_ClearsOnlyActiveTargetGeneratedSource() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-deployment-tests", Guid.NewGuid().ToString("N"));
        string deploymentRoot = Path.Combine(rootPath, "Deployment");
        string outputFolder = Path.Combine(rootPath, "legacy-output");
        CPPConversionOptions options = CreateTestOptions();
        options.DeploymentRoot = deploymentRoot;
        options.TargetId = "windows-directx";
        CPPDeploymentBuildLayout windowsLayout = CPPDeploymentBuildLayoutFactory.Create(options);
        options.TargetId = "ps2";
        CPPDeploymentBuildLayout ps2Layout = CPPDeploymentBuildLayoutFactory.Create(options);
        Directory.CreateDirectory(rootPath);
        try {
            TestableCPPCodeConverter converter = new TestableCPPCodeConverter(new CPPConversionRules(), options);
            converter.UseEmptyPInvokePlan();
            converter.WriteOutput(outputFolder);
            string ps2ConfigPath = Path.Combine(ps2Layout.GeneratedSourceRoot, CPPGeneratedConfigWriter.DefaultFileName);
            string ps2Config = File.ReadAllText(ps2ConfigPath);

            Directory.CreateDirectory(windowsLayout.GeneratedSourceRoot);
            Directory.CreateDirectory(windowsLayout.IntermediateRoot);
            Directory.CreateDirectory(ps2Layout.IntermediateRoot);
            Directory.CreateDirectory(windowsLayout.BuildRoot);
            Directory.CreateDirectory(outputFolder);
            string stalePath = Path.Combine(windowsLayout.GeneratedSourceRoot, "stale.cpp");
            string windowsIntermediatePath = Path.Combine(windowsLayout.IntermediateRoot, "preserved.obj");
            string ps2IntermediatePath = Path.Combine(ps2Layout.IntermediateRoot, "preserved.o");
            string buildPath = Path.Combine(windowsLayout.BuildRoot, "preserved.bin");
            string legacyPath = Path.Combine(outputFolder, "preserved.txt");
            File.WriteAllText(stalePath, "stale");
            File.WriteAllText(windowsIntermediatePath, "windows");
            File.WriteAllText(ps2IntermediatePath, "ps2");
            File.WriteAllText(buildPath, "build");
            File.WriteAllText(legacyPath, "legacy");

            options.TargetId = "windows-directx";
            converter.WriteOutput(outputFolder);

            Assert.False(File.Exists(stalePath));
            Assert.True(File.Exists(Path.Combine(windowsLayout.GeneratedSourceRoot, CPPGeneratedConfigWriter.DefaultFileName)));
            Assert.Equal(ps2Config, File.ReadAllText(ps2ConfigPath));
            Assert.Equal("windows", File.ReadAllText(windowsIntermediatePath));
            Assert.Equal("ps2", File.ReadAllText(ps2IntermediatePath));
            Assert.Equal("build", File.ReadAllText(buildPath));
            Assert.Equal("legacy", File.ReadAllText(legacyPath));
        } finally {
            Directory.Delete(rootPath, true);
        }
    }

    /// <summary>
    /// Ensures deployment path validation fails before cleanup can remove files selected by an escaping target.
    /// </summary>
    [Fact]
    public void WriteOutput_WithEscapingTarget_ThrowsBeforeDeletingOutput() {
        string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-deployment-tests", Guid.NewGuid().ToString("N"));
        string outsidePath = Path.Combine(rootPath, "outside");
        Directory.CreateDirectory(outsidePath);
        try {
            string sentinelPath = Path.Combine(outsidePath, "preserved.txt");
            File.WriteAllText(sentinelPath, "preserved");
            CPPConversionOptions options = CreateTestOptions();
            options.DeploymentRoot = Path.Combine(rootPath, "Deployment");
            options.TargetId = outsidePath;
            TestableCPPCodeConverter converter = new TestableCPPCodeConverter(new CPPConversionRules(), options);
            converter.UseEmptyPInvokePlan();

            Assert.Throws<ArgumentException>(() => converter.WriteOutput(outsidePath));

            Assert.Equal("preserved", File.ReadAllText(sentinelPath));
        } finally {
            Directory.Delete(rootPath, true);
        }
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

        /// <summary>
        /// Supplies the empty P/Invoke plan the analysis stage produces for a project without native imports, because
        /// these tests call <see cref="CPPCodeConverter.WriteOutput(string)"/> directly without running the pipeline.
        /// </summary>
        public void UseEmptyPInvokePlan() {
            SetPInvokePlan(new CPPPInvokePlan(Array.Empty<CPPPInvokeImport>(), Array.Empty<CPPPInvokeCallback>(), Array.Empty<CPPPInvokeMirrorStruct>()));
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
