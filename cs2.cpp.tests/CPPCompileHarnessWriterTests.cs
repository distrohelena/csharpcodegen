using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the generated compile harness for transpiled C++ output.
/// </summary>
public class CPPCompileHarnessWriterTests {
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
