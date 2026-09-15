namespace cs2.cpp.tests.TestHelpers;

/// <summary>
/// Describes one compiled-and-executed generated C++ program run, including the native compiler diagnostics.
/// </summary>
public sealed class CPPGeneratedProgramResult {
    /// <summary>
    /// Initializes a completed program run description.
    /// </summary>
    /// <param name="compilerExitCode">Exit code of the native compile-and-link step.</param>
    /// <param name="compilerOutput">Combined standard output and error of the native compile-and-link step.</param>
    /// <param name="programExitCode">Exit code of the executed program, or -1 when it was not run.</param>
    /// <param name="programOutput">Standard output captured from the executed program.</param>
    public CPPGeneratedProgramResult(int compilerExitCode, string compilerOutput, int programExitCode, string programOutput) {
        CompilerExitCode = compilerExitCode;
        CompilerOutput = compilerOutput ?? throw new ArgumentNullException(nameof(compilerOutput));
        ProgramExitCode = programExitCode;
        ProgramOutput = programOutput ?? throw new ArgumentNullException(nameof(programOutput));
    }

    /// <summary>
    /// Gets the exit code of the native compile-and-link step.
    /// </summary>
    public int CompilerExitCode { get; }

    /// <summary>
    /// Gets the combined compiler and linker diagnostics.
    /// </summary>
    public string CompilerOutput { get; }

    /// <summary>
    /// Gets the exit code of the executed program, or -1 when compilation failed and nothing ran.
    /// </summary>
    public int ProgramExitCode { get; }

    /// <summary>
    /// Gets the standard output captured from the executed program.
    /// </summary>
    public string ProgramOutput { get; }

    /// <summary>
    /// Gets the program output split into trimmed non-empty lines for line-oriented assertions.
    /// </summary>
    public IReadOnlyList<string> ProgramOutputLines {
        get {
            return ProgramOutput
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length > 0)
                .ToList();
        }
    }
}
