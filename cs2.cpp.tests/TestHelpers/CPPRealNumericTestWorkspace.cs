using System.Security;
using System.Runtime.CompilerServices;

namespace cs2.cpp.tests.TestHelpers;

/// <summary>Converts the actual engine scalar source files through a fixed scratch fixture without exporting the checkout.</summary>
public sealed class CPPRealNumericTestWorkspace {
    /// <summary>Creates one source fixture in the explicit task scratch directory and converts it with the production pipeline.</summary>
    /// <param name="source">Consumer source that exercises the real engine scalar contracts.</param>
    /// <param name="storageBits">Native stored numeric width.</param>
    /// <param name="arithmeticBits">Native expression evaluation width.</param>
    /// <param name="includeEngineScalars">Whether this fixture consumes the actual engine scalar source files.</param>
    /// <param name="callerFilePath">Source location used to locate the real sibling engine checkout.</param>
    /// <returns>Generated conversion output with ordinary disposable fixture ownership.</returns>
    public CPPOwnershipConversionOutput Convert(string source, int storageBits, int arithmeticBits,
        bool includeEngineScalars = true, [CallerFilePath] string callerFilePath = "") {
        string workspace = @"C:\temp\numeric-precision\windows\converter-fixture";
        string project = Path.Combine(workspace, "Fixture.csproj");
        string output = Path.Combine(workspace, "generated");
        string repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFilePath)!, ".."));
        string engine = Path.GetFullPath(Path.Combine(repository, "..", "helengine"));
        Directory.CreateDirectory(workspace);
        string items = string.Empty;
        string import = string.Empty;
        if (includeEngineScalars) {
            foreach (string file in new[] { "Real.cs", "RealEvaluation.cs", "RealMath.cs", "NumericPrecisionPolicy.cs" }) {
                string path = Path.Combine(engine, "engine", "helengine.core", "numeric", file);
                if (!File.Exists(path)) { throw new FileNotFoundException("The numeric fixture requires the real sibling engine checkout.", path); }
                items += "<Compile Include=\"" + SecurityElement.Escape(path) + "\" />";
            }
            import = "<Import Project=\"" + SecurityElement.Escape(Path.Combine(engine, "build", "NumericPrecision.targets")) + "\" />";
        }
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><HelengineRealStorageBits>64</HelengineRealStorageBits><HelengineRealArithmeticBits>64</HelengineRealArithmeticBits></PropertyGroup><ItemGroup>" + items + "</ItemGroup>" + import + "</Project>");
        File.WriteAllText(Path.Combine(workspace, "Fixture.cs"), source);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PlatformOptionValues = new Dictionary<string, string> {
            ["real-storage-bits"] = storageBits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["real-arithmetic-bits"] = arithmeticBits.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.AddCsproj(project);
        converter.WriteOutput(output);
        string generatedText = string.Join(Environment.NewLine, Directory.GetFiles(output, "*.hpp", SearchOption.AllDirectories).Select(File.ReadAllText));
        return new CPPOwnershipConversionOutput(workspace, project, output, generatedText, converter.Report, converter);
    }
}
