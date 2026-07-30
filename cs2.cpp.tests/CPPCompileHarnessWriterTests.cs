using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the generated compile harness for transpiled C++ output.
/// </summary>
public class CPPCompileHarnessWriterTests {
    /// <summary>
    /// Ensures the generated MSVC script preserves the unity source as a distinct compiler argument when its script directory ends with a backslash.
    /// </summary>
    [Fact]
    public void Write_MsvcScriptPassesUnitySourceAsDistinctCompilerArgument() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        string outputFolder = Path.Combine(Path.GetTempPath(), "compile-harness-" + Guid.NewGuid().ToString("N"));
        string fakeCompilerFolder = Path.Combine(outputFolder, "fake-compiler");
        string compilerArgumentsPath = Path.Combine(outputFolder, "compiler-arguments.txt");
        string compilerCaptureScriptPath = Path.Combine(fakeCompilerFolder, "capture-compiler-arguments.ps1");
        Directory.CreateDirectory(fakeCompilerFolder);
        File.WriteAllText(Path.Combine(outputFolder, "Alpha.cpp"), "int alpha = 1;" + Environment.NewLine);
        File.WriteAllText(
            compilerCaptureScriptPath,
            "if ($args.Count -lt 7) { exit 2 }\r\n"
            + "Set-Content -LiteralPath $env:CL_ARGUMENTS_LOG -Value $args[6]\r\n");
        File.WriteAllText(
            Path.Combine(fakeCompilerFolder, "cl.cmd"),
            "@echo off\r\n"
            + "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%CL_CAPTURE_SCRIPT%\" %*\r\n");

        CPPCompileHarnessWriter.Write(outputFolder, CPPConversionOptions.CreateDefault());
        string msvcPath = Path.Combine(outputFolder, CPPCompileHarnessWriter.MsvcBuildScriptFileName);
        string commandProcessorPath = Environment.GetEnvironmentVariable("ComSpec")
            ?? throw new InvalidOperationException("The Windows command processor path is required to verify the generated MSVC script.");
        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = commandProcessorPath,
            WorkingDirectory = outputFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(msvcPath);
        startInfo.Environment["PATH"] = fakeCompilerFolder + Path.PathSeparator + startInfo.Environment["PATH"];
        startInfo.Environment["CL_ARGUMENTS_LOG"] = compilerArgumentsPath;
        startInfo.Environment["CL_CAPTURE_SCRIPT"] = compilerCaptureScriptPath;

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("The generated MSVC script process could not be started.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"The generated MSVC script exited with code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        Assert.Contains(Path.Combine(outputFolder, CPPCompileHarnessWriter.UnityFileName), File.ReadAllLines(compilerArgumentsPath));
    }

    /// <summary>
    /// Ensures the compile harness emits a unity translation unit and compiler entry scripts.
    /// </summary>
    [Fact]
    public void Write_WithGeneratedSources_WritesUnityAndCompilerScripts() {
        string outputFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputFolder);
        Directory.CreateDirectory(Path.Combine(outputFolder, "nested"));

        File.WriteAllText(Path.Combine(outputFolder, "Alpha.cpp"), "int alpha = 1;" + Environment.NewLine);
        File.WriteAllText(Path.Combine(outputFolder, "nested", "Beta.cpp"), "int beta = 2;" + Environment.NewLine);

        IReadOnlyList<string> emittedFiles = CPPCompileHarnessWriter.Write(outputFolder, CPPConversionOptions.CreateDefault());
        string unityPath = Path.Combine(outputFolder, CPPCompileHarnessWriter.UnityFileName);
        string gccPath = Path.Combine(outputFolder, CPPCompileHarnessWriter.GccBuildScriptFileName);
        string msvcPath = Path.Combine(outputFolder, CPPCompileHarnessWriter.MsvcBuildScriptFileName);

        Assert.Contains(unityPath, emittedFiles);
        Assert.Contains(gccPath, emittedFiles);
        Assert.Contains(msvcPath, emittedFiles);
        Assert.Contains("#include \"Alpha.cpp\"", File.ReadAllText(unityPath));
        Assert.Contains("#include \"nested/Beta.cpp\"", File.ReadAllText(unityPath));
        Assert.Contains("g++ -std=c++20", File.ReadAllText(gccPath));
        Assert.Contains("cl /nologo /std:c++20", File.ReadAllText(msvcPath));
    }
}
