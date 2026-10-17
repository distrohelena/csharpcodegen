using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests {
    /// <summary>Verifies native contracts exercised by aliased content streams.</summary>
    public sealed class CPPAliasRuntimeRegressionTests {
        /// <summary>Ensures dictionary enumeration materializes managed key/value entries.</summary>
        [Theory]
        [InlineData("Dictionary")]
        [InlineData("IDictionary")]
        [InlineData("IReadOnlyDictionary")]
        public void DictionaryForeach_UsesManagedEntry(string dictionaryType) {
            string source = """
                using System.Collections.Generic;
                public class AliasProbe {
                    public string Find(DICTIONARY<string,string> aliases) {
                        foreach (KeyValuePair<string,string> alias in aliases) {
                            if (alias.Key == "logical") return alias.Value;
                        }
                        return "missing";
                    }
                }
                """;
            string output = ConvertSource(source.Replace("DICTIONARY", dictionaryType));
            string generated = File.ReadAllText(Path.Combine(output, "AliasProbe.cpp"));
            Assert.Matches(@"KeyValuePair<[^>]+> alias\(", generated);
            CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output).Run("""
                Dictionary<HeCppString, HeCppString> aliases;
                AliasProbe probe;
                if (probe.Find(&aliases) != "missing") return 1;
                aliases.Add("other", "ignored");
                aliases.Add("logical", "resolved");
                if (probe.Find(&aliases) != "resolved") return 2;
                aliases.set_Item("logical", HeCppString(nullptr));
                if (!probe.Find(&aliases).is_null()) return 3;
                aliases.set_Item("logical", HeCppString(""));
                if (probe.Find(&aliases).is_null() || !probe.Find(&aliases).empty()) return 4;
                """);
            Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
            Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
        }

        /// <summary>Preserves scalar iteration over dictionary key and value views.</summary>
        [Theory]
        [InlineData("Dictionary", "Keys")]
        [InlineData("Dictionary", "Values")]
        [InlineData("IDictionary", "Keys")]
        [InlineData("IDictionary", "Values")]
        [InlineData("IReadOnlyDictionary", "Keys")]
        [InlineData("IReadOnlyDictionary", "Values")]
        public void DictionaryViews_DoNotMaterializeEntries(string dictionaryType, string view) {
            string source = "using System.Collections.Generic; public class ViewProbe { public int Count(DICTIONARY<string,int> values) { int count = 0; foreach (var value in values.VIEW) { count++; } return count; } }";
            string output = ConvertSource(source.Replace("DICTIONARY", dictionaryType).Replace("VIEW", view));
            string generated = File.ReadAllText(Path.Combine(output, "ViewProbe.cpp"));
            Assert.DoesNotContain("__dictionaryEntry_", generated, StringComparison.Ordinal);
            Assert.Contains("for (const auto& value :", generated, StringComparison.Ordinal);
        }
        /// <summary>Ensures disposed-object failures have a native runtime exception contract.</summary>
        [Fact]
        public void ObjectDisposedException_HasNativeRuntimeContract() {
            string output = ConvertSource("using System; public class DisposedProbe { public void Check() { throw new ObjectDisposedException(nameof(DisposedProbe)); } }");
            Assert.Contains("object_disposed_exception.hpp", File.ReadAllText(Path.Combine(output, "runtime", "native_exceptions.hpp")), StringComparison.Ordinal);
            string runtime = File.ReadAllText(Path.Combine(output, "system", "object_disposed_exception.hpp"));
            Assert.Contains("class ObjectDisposedException : public InvalidOperationException", runtime, StringComparison.Ordinal);
        }

        /// <summary>Converts an isolated fixture using the same runtime templates as production builds.</summary>
        static string ConvertSource(string source) {
            string root = Path.Combine(Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT") ?? @"C:\dev\helworks\builds\demodisc-worktree-integration-20261015\tests", "alias-regressions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string project = Path.Combine(root, "Fixture.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><LangVersion>preview</LangVersion><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Fixture.cs"), source);
            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.FeatureCatalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();
            options.BuildFeatureProfile.WithMode("host_file_system", CPPFeatureMode.Enabled).WithMode("reflection_like_runtime", CPPFeatureMode.Enabled).WithMode("text_processing", CPPFeatureMode.Enabled);
            CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
            converter.AddCsproj(project);
            string output = Path.Combine(root, "out");
            converter.WriteOutput(output);
            return output;
        }
    }
}
