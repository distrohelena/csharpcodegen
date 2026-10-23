using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Executes native dictionary lookups against the managed contract for missing and present keys.</summary>
public sealed class CPPDictionaryTryGetValueRuntimeTests {
    /// <summary>Requires a failed lookup to replace prior out values with default, including null strings and references.</summary>
    [Fact]
    public void MissingKey_ReplacesOutValueWithDefault_AndPresentKeyReturnsStoredValue() {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "csharpcodegen");
        string fixtureRoot = Path.Combine(artifactRoot, "dictionary-try-get-value");
        string outputRoot = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            public sealed class ReferenceValue { }
            public struct PairValue { public int X; public int Y; }
            public static class DictionaryFixture {
                public static bool TryRead(Dictionary<int, int> values, int key, out int value) {
                    return values.TryGetValue(key, out value);
                }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputRoot);
        Assert.False(converter.Report.HasErrors);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(outputRoot).Run("""
            Dictionary<int32_t, int32_t> integers;
            int32_t number = 71;
            if (DictionaryFixture::TryRead__out2(&integers, 3, number) || number != 0) return 1;
            integers.Add(3, 9);
            number = 71;
            if (!DictionaryFixture::TryRead__out2(&integers, 3, number) || number != 9) return 2;
            Dictionary<int32_t, ReferenceValue*> references;
            ReferenceValue marker;
            ReferenceValue* reference = &marker;
            if (references.TryGetValue(3, reference) || reference != nullptr) return 3;
            references.Add(3, &marker);
            if (!references.TryGetValue(3, reference) || reference != &marker) return 4;
            Dictionary<int32_t, HeCppString> strings;
            HeCppString text("sentinel");
            if (strings.TryGetValue(3, text) || !text.is_null()) return 5;
            strings.Add(3, HeCppString("present"));
            if (!strings.TryGetValue(3, text) || text != HeCppString("present")) return 6;
            Dictionary<int32_t, PairValue> pairs;
            PairValue pair;
            pair.X = 17;
            pair.Y = 29;
            if (pairs.TryGetValue(3, pair) || pair.X != 0 || pair.Y != 0) return 7;
            std::cout << "dictionary-defaults-ok\n";
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native exit {result.ProgramExitCode}: {result.ProgramOutput}");
        Assert.Equal("dictionary-defaults-ok", result.ProgramOutput.Trim());
    }
}
