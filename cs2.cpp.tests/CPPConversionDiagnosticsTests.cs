using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies actionable unsupported-construct diagnostics for the C++ backend.
/// </summary>
public class CPPConversionDiagnosticsTests {
    /// <summary>
    /// Ensures the converter can record a structured unsupported-construct diagnostic for later reporting.
    /// </summary>
    [Fact]
    public void ReportUnsupportedConstruct_AddsStructuredErrorDiagnostic() {
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), CreateTestOptions());

        converter.ReportUnsupportedConstruct(
            "Player",
            "Tick",
            "UnsafeStatement",
            "The C++ backend does not support unsafe statements in the headless core profile.",
            "Replace the unsafe block with a runtime helper or platform adapter.",
            "Player.cs");

        CPPConversionDiagnostic diagnostic = Assert.Single(converter.Report.Diagnostics);
        Assert.Equal(CPPDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("CPP1000", diagnostic.Code);
        Assert.Equal("Player", diagnostic.SourceTypeName);
        Assert.Equal("Tick", diagnostic.SourceMemberName);
        Assert.Equal("UnsafeStatement", diagnostic.SyntaxKind);
        Assert.Equal("Replace the unsafe block with a runtime helper or platform adapter.", diagnostic.Recommendation);
        Assert.Equal("Player.cs", diagnostic.FilePath);
    }

    /// <summary>
    /// Ensures the JSON report includes the remediation hint for unsupported constructs.
    /// </summary>
    [Fact]
    public void WriteReport_WithDiagnosticRecommendation_SerializesRecommendation() {
        CPPConversionReport report = new CPPConversionReport();
        report.Diagnostics.Add(new CPPConversionDiagnostic {
            Severity = CPPDiagnosticSeverity.Error,
            Code = "CPP1000",
            Message = "stackalloc is not supported in the minimal runtime profile.",
            SourceTypeName = "BufferBuilder",
            SourceMemberName = "Create",
            SyntaxKind = "StackAllocArrayCreationExpression",
            Recommendation = "Replace stackalloc with an explicit native buffer abstraction.",
            FilePath = "BufferBuilder.cs"
        });

        string outputFolder = Path.Combine(Path.GetTempPath(), "cs2.cpp.tests", Guid.NewGuid().ToString("N"));
        string reportPath = CPPConversionReportWriter.Write(outputFolder, report);
        string json = File.ReadAllText(reportPath);

        Assert.Contains("\"recommendation\": \"Replace stackalloc with an explicit native buffer abstraction.\"", json);
    }

    /// <summary>
    /// Creates focused converter options that avoid external runtime metadata tooling during unit tests.
    /// </summary>
    /// <returns>The option set used by the diagnostic tests.</returns>
    static CPPConversionOptions CreateTestOptions() {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        return options;
    }
}
