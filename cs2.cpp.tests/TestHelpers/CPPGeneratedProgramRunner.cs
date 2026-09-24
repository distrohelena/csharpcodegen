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
        return Run(entryPointBody, string.Empty);
    }

    /// <summary>
    /// Writes the entry point with a caller-supplied prelude, compiles and links it against the generated unity source
    /// (plus the isolated native-imports translation unit and its link libraries when present), and runs the executable.
    /// </summary>
    /// <param name="entryPointBody">C++ statements placed inside <c>main</c>; the generated unity source and iostream are already included.</param>
    /// <param name="entryPointPrelude">
    /// C++ text written before the generated unity include, for example a platform header the unity build must tolerate;
    /// pass <see cref="string.Empty"/> for none.
    /// </param>
    /// <returns>Compiler diagnostics plus the executed program's exit code and standard output.</returns>
    public CPPGeneratedProgramResult Run(string entryPointBody, string entryPointPrelude) {
        return RunWithDeveloperCommandArguments(entryPointBody, entryPointPrelude, string.Empty);
    }

    /// <summary>
    /// Writes the entry point with a caller-supplied prelude, compiles and links it for an explicit target architecture,
    /// and runs the executable. On Windows the architecture is passed to <c>VsDevCmd.bat</c> as <c>-arch=</c>, so the same
    /// generated output can be verified as both a 32-bit and a 64-bit program.
    /// </summary>
    /// <param name="entryPointBody">C++ statements placed inside <c>main</c>; the generated unity source and iostream are already included.</param>
    /// <param name="entryPointPrelude">
    /// C++ text written before the generated unity include; pass <see cref="string.Empty"/> for none.
    /// </param>
    /// <param name="targetArchitecture">VsDevCmd target architecture, for example <c>x86</c> or <c>amd64</c>.</param>
    /// <returns>Compiler diagnostics plus the executed program's exit code and standard output.</returns>
    public CPPGeneratedProgramResult Run(string entryPointBody, string entryPointPrelude, string targetArchitecture) {
        if (string.IsNullOrWhiteSpace(targetArchitecture)) {
            throw new ArgumentException("A target architecture is required to select the MSVC toolset.", nameof(targetArchitecture));
        }

        return RunWithDeveloperCommandArguments(entryPointBody, entryPointPrelude, " -arch=" + targetArchitecture);
    }

    /// <summary>
    /// Writes the entry point, compiles and links it against the generated sources, and runs the executable.
    /// </summary>
    /// <param name="entryPointBody">C++ statements placed inside <c>main</c>.</param>
    /// <param name="entryPointPrelude">C++ text written before the generated unity include; empty for none.</param>
    /// <param name="developerCommandArguments">
    /// Extra arguments appended to the <c>VsDevCmd.bat</c> call on Windows (for example <c> -arch=x86</c>), or
    /// <see cref="string.Empty"/> to use the developer command's default architecture.
    /// </param>
    /// <returns>Compiler diagnostics plus the executed program's exit code and standard output.</returns>
    CPPGeneratedProgramResult RunWithDeveloperCommandArguments(string entryPointBody, string entryPointPrelude, string developerCommandArguments) {
        if (string.IsNullOrWhiteSpace(entryPointBody)) {
            throw new ArgumentException("An entry point body is required to run a generated program.", nameof(entryPointBody));
        }
        if (entryPointPrelude == null) {
            throw new ArgumentNullException(nameof(entryPointPrelude));
        }

        string buildDirectory = Path.Combine(OutputPath, "build", "program");
        Directory.CreateDirectory(buildDirectory);
        string entryPointPath = Path.Combine(OutputPath, EntryPointFileName);
        string programPath = Path.Combine(buildDirectory, ProgramFileName);
        File.WriteAllText(entryPointPath, BuildEntryPointSource(entryPointBody, entryPointPrelude));

        int compilerExitCode = RunProcess(CreateCompilerStartInfo(entryPointPath, programPath, buildDirectory, developerCommandArguments), out string compilerOutput);
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
    /// Builds the entry-point translation unit that writes the caller's prelude, then includes the generated unity
    /// source, then defines <c>main</c> around the caller's body.
    /// </summary>
    /// <param name="entryPointBody">Statements executed inside <c>main</c>.</param>
    /// <param name="entryPointPrelude">Text written before the generated unity include; empty for none.</param>
    /// <returns>Complete C++ source text.</returns>
    static string BuildEntryPointSource(string entryPointBody, string entryPointPrelude) {
        List<string> lines = new List<string> { "// Generated execution-validation entry point." };
        if (entryPointPrelude.Length > 0) {
            lines.Add(entryPointPrelude);
        }
        lines.AddRange(new[] {
            "#include \"" + cs2.cpp.CPPCompileHarnessWriter.UnityFileName + "\"",
            "#include <iostream>",
            string.Empty,
            "int main() {",
            entryPointBody,
            "return 0;",
            "}",
            string.Empty
        });
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Creates the platform-specific compile-and-link process for the entry point and generated sources. When the output
    /// contains the isolated native-imports source it is compiled as its own translation unit, and on Windows every
    /// library published by the handoff contract's <c>CPP_GENERATED_NATIVE_LINK_LIBRARIES</c> is linked.
    /// </summary>
    /// <param name="entryPointPath">Entry-point translation unit path.</param>
    /// <param name="programPath">Executable output path.</param>
    /// <param name="buildDirectory">Directory receiving intermediate objects.</param>
    /// <param name="developerCommandArguments">Extra arguments appended to the <c>VsDevCmd.bat</c> call on Windows.</param>
    /// <returns>A redirected, non-interactive compiler process configuration.</returns>
    System.Diagnostics.ProcessStartInfo CreateCompilerStartInfo(string entryPointPath, string programPath, string buildDirectory, string developerCommandArguments) {
        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo {
            WorkingDirectory = buildDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string nativeImportsSourcePath = Path.Combine(OutputPath, cs2.cpp.CPPNativeImportsWriter.FolderName, cs2.cpp.CPPNativeImportsWriter.SourceFileName);
        bool hasNativeImportsSource = File.Exists(nativeImportsSourcePath);
        if (OperatingSystem.IsWindows()) {
            string developerCommandPath = CPPOwnershipConversionOutput.ResolveVisualStudioDeveloperCommandPath();
            string compileScriptPath = Path.Combine(buildDirectory, "run_program_compile.cmd");
            string nativeImportsArgument = hasNativeImportsSource ? $" \"{nativeImportsSourcePath}\"" : string.Empty;
            IReadOnlyList<string> linkLibraries = CPPHandoffLinkLibraryReader.Read(Path.Combine(OutputPath, cs2.cpp.CPPWindowsHandoffWriter.FileName));
            string linkLibraryArguments = string.Concat(linkLibraries.Select(library => " " + library + ".lib"));
            File.WriteAllText(
                compileScriptPath,
                "@echo off\r\n"
                + $"call \"{developerCommandPath}\" -no_logo{developerCommandArguments}\r\n"
                + "if errorlevel 1 exit /b %errorlevel%\r\n"
                + $"cl /nologo /std:c++20 /EHsc /I\"{OutputPath}\" /I\"{Path.Combine(OutputPath, "runtime")}\" "
                + $"/Fo\"{buildDirectory}\\\\\" \"{entryPointPath}\"{nativeImportsArgument} /Fe\"{programPath}\"{linkLibraryArguments}\r\n"
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
            if (hasNativeImportsSource) {
                startInfo.ArgumentList.Add(nativeImportsSourcePath);
            }
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
