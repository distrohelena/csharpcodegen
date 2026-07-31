using cs2.cpp;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Describes one workspace-owned ownership conversion and exposes its generated sources, report, and compiler entry point.
/// </summary>
public sealed class CPPOwnershipConversionOutput : IDisposable {
    /// <summary>
    /// Tracks whether this result has already released its workspace-owned artifacts.
    /// </summary>
    bool Disposed;

    /// <summary>
    /// Initializes a completed ownership conversion result.
    /// </summary>
    /// <param name="workspacePath">Workspace root containing the source fixture and generated output.</param>
    /// <param name="projectPath">Generated source project passed to the converter.</param>
    /// <param name="outputPath">Directory containing generated C++ and compile harness files.</param>
    /// <param name="generatedText">Deterministically concatenated generated C++ source and header text.</param>
    /// <param name="report">Structured conversion report produced by the converter.</param>
    /// <param name="converter">Converter instance that produced this output.</param>
    public CPPOwnershipConversionOutput(
        string workspacePath,
        string projectPath,
        string outputPath,
        string generatedText,
        CPPConversionReport report,
        CPPCodeConverter converter) {
        WorkspacePath = workspacePath ?? throw new ArgumentNullException(nameof(workspacePath));
        ProjectPath = projectPath ?? throw new ArgumentNullException(nameof(projectPath));
        OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        GeneratedText = generatedText ?? throw new ArgumentNullException(nameof(generatedText));
        Report = report ?? throw new ArgumentNullException(nameof(report));
        Converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }

    /// <summary>
    /// Gets the workspace root containing the complete test fixture.
    /// </summary>
    public string WorkspacePath { get; }

    /// <summary>
    /// Gets the generated C# project path consumed by the converter.
    /// </summary>
    public string ProjectPath { get; }

    /// <summary>
    /// Gets the generated C++ output directory.
    /// </summary>
    public string OutputPath { get; }

    /// <summary>
    /// Gets all generated implementation and header text in deterministic path order.
    /// </summary>
    public string GeneratedText { get; }

    /// <summary>
    /// Gets the structured conversion report captured for the fixture.
    /// </summary>
    public CPPConversionReport Report { get; }

    /// <summary>
    /// Gets the converter instance so tests can inspect semantic ownership analysis state.
    /// </summary>
    public CPPCodeConverter Converter { get; }

    /// <summary>
    /// Compiles the generated unity translation unit through the platform compile harness.
    /// </summary>
    /// <param name="compilerOutput">Combined standard output and standard error from the native compiler process.</param>
    /// <returns>The native compiler process exit code.</returns>
    public int CompileGeneratedOutput(out string compilerOutput) {
        System.Diagnostics.ProcessStartInfo startInfo = CreateCompilerStartInfo();
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("The generated ownership compile harness process could not be started.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WhenAll(standardOutputTask, standardErrorTask).GetAwaiter().GetResult();
        string standardOutput = standardOutputTask.Result;
        string standardError = standardErrorTask.Result;
        compilerOutput = standardOutput + Environment.NewLine + standardError;
        return process.ExitCode;
    }

    /// <summary>
    /// Removes the isolated repository-owned fixture directory and all generated artifacts it contains.
    /// </summary>
    public void Dispose() {
        if (Disposed) {
            return;
        }

        Disposed = true;
        if (Directory.Exists(WorkspacePath)) {
            Directory.Delete(WorkspacePath, true);
        }
    }

    /// <summary>
    /// Creates a platform-specific native compiler process for the generated harness.
    /// </summary>
    /// <returns>A redirected, non-interactive compiler process configuration.</returns>
    System.Diagnostics.ProcessStartInfo CreateCompilerStartInfo() {
        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo {
            WorkingDirectory = OutputPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (OperatingSystem.IsWindows()) {
            string developerCommandPath = ResolveVisualStudioDeveloperCommandPath();
            string compileScriptPath = Path.Combine(OutputPath, "run_ownership_compile.cmd");
            File.WriteAllText(
                compileScriptPath,
                "@echo off\r\n"
                + $"call \"{developerCommandPath}\" -no_logo\r\n"
                + "if errorlevel 1 exit /b %errorlevel%\r\n"
                + $"call \"{Path.Combine(OutputPath, CPPCompileHarnessWriter.MsvcBuildScriptFileName)}\"\r\n"
                + "exit /b %errorlevel%\r\n");
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("The Windows command processor path is required for native ownership compilation.");
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(compileScriptPath);
        } else {
            startInfo.FileName = "/usr/bin/env";
            startInfo.ArgumentList.Add("bash");
            startInfo.ArgumentList.Add(Path.Combine(OutputPath, CPPCompileHarnessWriter.GccBuildScriptFileName));
        }

        return startInfo;
    }

    /// <summary>
    /// Resolves the newest installed Visual Studio developer command script used to configure the MSVC environment.
    /// </summary>
    /// <returns>The absolute developer command script path.</returns>
    static string ResolveVisualStudioDeveloperCommandPath() {
        string visualStudioRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft Visual Studio",
            "2022");
        if (!Directory.Exists(visualStudioRoot)) {
            throw new InvalidOperationException("Visual Studio 2022 is required for native ownership compilation on Windows.");
        }

        string[] developerCommandPaths = Directory.GetFiles(
                visualStudioRoot,
                "VsDevCmd.bat",
                SearchOption.AllDirectories)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (developerCommandPaths.Length == 0) {
            throw new InvalidOperationException("Visual Studio VsDevCmd.bat could not be located for native ownership compilation.");
        }

        return developerCommandPaths[0];
    }
}
