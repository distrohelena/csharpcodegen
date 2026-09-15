namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Compiles a generated C++ output directory together with a caller-supplied entry point and executes the native program,
/// so tests can verify runtime behavior instead of only emitted text.
/// </summary>
public sealed class CPPGeneratedProgramRunner {
    /// <summary>
    /// File name of the entry-point translation unit written next to the generated unity source.
    /// </summary>
    const string EntryPointFileName = "program_main.cpp";

    /// <summary>
    /// File name of the produced native executable.
    /// </summary>
    const string ProgramFileName = "generated_program.exe";

    /// <summary>
    /// Generated output directory that contains the generated sources, runtime headers, and compile harness.
    /// </summary>
    readonly string OutputPath;

    /// <summary>
    /// Initializes a runner for one generated output directory.
    /// </summary>
    /// <param name="outputPath">Generated C++ output directory produced by the converter.</param>
    public CPPGeneratedProgramRunner(string outputPath) {
        OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
    }

    /// <summary>
    /// Writes the entry point, compiles and links it against the generated unity source, and runs the executable.
    /// </summary>
    /// <param name="entryPointBody">C++ statements placed inside <c>main</c>; the generated unity source and iostream are already included.</param>
    /// <returns>Compiler diagnostics plus the executed program's exit code and standard output.</returns>
    public CPPGeneratedProgramResult Run(string entryPointBody) {
        if (string.IsNullOrWhiteSpace(entryPointBody)) {
            throw new ArgumentException("An entry point body is required to run a generated program.", nameof(entryPointBody));
        }

        string buildDirectory = Path.Combine(OutputPath, "build", "program");
        Directory.CreateDirectory(buildDirectory);
        string entryPointPath = Path.Combine(OutputPath, EntryPointFileName);
        string programPath = Path.Combine(buildDirectory, ProgramFileName);
        File.WriteAllText(entryPointPath, BuildEntryPointSource(entryPointBody));

        int compilerExitCode = RunProcess(CreateCompilerStartInfo(entryPointPath, programPath, buildDirectory), out string compilerOutput);
        if (compilerExitCode != 0) {
            return new CPPGeneratedProgramResult(compilerExitCode, compilerOutput, -1, string.Empty);
        }

        System.Diagnostics.ProcessStartInfo programStartInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = programPath,
            WorkingDirectory = buildDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        int programExitCode = RunProcess(programStartInfo, out string programOutput);
        return new CPPGeneratedProgramResult(compilerExitCode, compilerOutput, programExitCode, programOutput);
    }

    /// <summary>
    /// Builds the entry-point translation unit that includes the generated unity source before the caller's main body.
    /// </summary>
    /// <param name="entryPointBody">Statements executed inside <c>main</c>.</param>
    /// <returns>Complete C++ source text.</returns>
    static string BuildEntryPointSource(string entryPointBody) {
        return string.Join(Environment.NewLine, new[] {
            "// Generated execution-validation entry point.",
            "#include \"" + cs2.cpp.CPPCompileHarnessWriter.UnityFileName + "\"",
            "#include <iostream>",
            string.Empty,
            "int main() {",
            entryPointBody,
            "return 0;",
            "}",
            string.Empty
        });
    }

    /// <summary>
    /// Creates the platform-specific compile-and-link process for the entry point and generated sources.
    /// </summary>
    /// <param name="entryPointPath">Entry-point translation unit path.</param>
    /// <param name="programPath">Executable output path.</param>
    /// <param name="buildDirectory">Directory receiving intermediate objects.</param>
    /// <returns>A redirected, non-interactive compiler process configuration.</returns>
    System.Diagnostics.ProcessStartInfo CreateCompilerStartInfo(string entryPointPath, string programPath, string buildDirectory) {
        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo {
            WorkingDirectory = buildDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (OperatingSystem.IsWindows()) {
            string developerCommandPath = CPPOwnershipConversionOutput.ResolveVisualStudioDeveloperCommandPath();
            string compileScriptPath = Path.Combine(buildDirectory, "run_program_compile.cmd");
            File.WriteAllText(
                compileScriptPath,
                "@echo off\r\n"
                + $"call \"{developerCommandPath}\" -no_logo\r\n"
                + "if errorlevel 1 exit /b %errorlevel%\r\n"
                + $"cl /nologo /std:c++20 /EHsc /I\"{OutputPath}\" /I\"{Path.Combine(OutputPath, "runtime")}\" "
                + $"/Fo\"{buildDirectory}\\\\\" \"{entryPointPath}\" /Fe\"{programPath}\"\r\n"
                + "exit /b %errorlevel%\r\n");
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("The Windows command processor path is required for native program compilation.");
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(compileScriptPath);
        } else {
            startInfo.FileName = "/usr/bin/env";
            startInfo.ArgumentList.Add("g++");
            startInfo.ArgumentList.Add("-std=c++20");
            startInfo.ArgumentList.Add("-I" + OutputPath);
            startInfo.ArgumentList.Add("-I" + Path.Combine(OutputPath, "runtime"));
            startInfo.ArgumentList.Add(entryPointPath);
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(programPath);
        }

        return startInfo;
    }

    /// <summary>
    /// Runs one redirected process to completion and captures its combined output.
    /// </summary>
    /// <param name="startInfo">Process configuration with redirected standard streams.</param>
    /// <param name="combinedOutput">Standard output followed by standard error.</param>
    /// <returns>The process exit code.</returns>
    static int RunProcess(System.Diagnostics.ProcessStartInfo startInfo, out string combinedOutput) {
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("The generated program process could not be started.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WhenAll(standardOutputTask, standardErrorTask).GetAwaiter().GetResult();
        combinedOutput = standardOutputTask.Result + Environment.NewLine + standardErrorTask.Result;
        return process.ExitCode;
    }
}
