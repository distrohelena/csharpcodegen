using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Executes converted C# to catch loss of the distinction between a null string reference and an empty string value.</summary>
public sealed class CPPManagedStringNullSemanticsTests {
    /// <summary>Source exercises null semantics through managed parameters, storage, operators, and a platform-settings fallback guard.</summary>
    const string FixtureSource = """
        public class ManagedStringNullFixture {
            /// <summary>Uninitialized managed string storage must begin as null.</summary>
            public string DefaultField;
            /// <summary>Uninitialized static managed string storage must begin as null.</summary>
            public static string DefaultStatic;
            /// <summary>Records the exact order and count of evaluated managed operands.</summary>
            public static int Trace;
            /// <summary>Exposes default managed storage through generated accessors.</summary>
            public string DefaultProperty { get; set; }
            /// <summary>Returns a null literal through a computed generated getter.</summary>
            public string NullGetter => null;
            /// <summary>Returns whether the parameter is the null reference.</summary>
            public static bool IsNull(string value) { return value == null; }
            /// <summary>Returns whether the parameter is a non-null reference.</summary>
            public static bool IsNotNull(string value) { return value != null; }
            /// <summary>Checks the same null operator with the literal on the left.</summary>
            public static bool IsNullLeft(string value) { return null == value; }
            /// <summary>Checks the reversed inequality operator.</summary>
            public static bool IsNotNullLeft(string value) { return null != value; }
            /// <summary>Compares two managed values, including two null references.</summary>
            public static bool Equal(string left, string right) { return left == right; }
            /// <summary>Preserves an empty value and substitutes only a null reference.</summary>
            public static string Fallback(string value) { return value ?? "fallback"; }
            /// <summary>Concatenates managed operands with C# null-as-empty behavior.</summary>
            public static string Concat(string left, string right) { return left + right; }
            /// <summary>Models the platform settings lookup whose null query must choose the default branch.</summary>
            public static string PlatformSetting(string query, string fallback) {
                if (query == null) return fallback;
                return "query:" + query;
            }
            /// <summary>Records one operand evaluation and returns the requested managed value.</summary>
            public static string Mark(string value, int digit) { Trace = Trace * 10 + digit; return value; }
            /// <summary>Records one assignment-target index evaluation.</summary>
            public static int MarkIndex() { Trace = Trace * 10 + 1; return 0; }
            /// <summary>Produces a null reference through an ordinary generated method return.</summary>
            public static string ReturnNull() { return null; }
            /// <summary>Produces the C# default string reference explicitly.</summary>
            public static string ReturnDefault() { return default(string); }
            /// <summary>Checks lazy fallback expressions and assignment without collapsing empty into null.</summary>
            public static int CheckLazy() {
                Trace = 0;
                string kept = Mark("", 1) ?? Mark("fallback", 2);
                if (kept != "" || Trace != 1) return 21;
                Trace = 0;
                string replaced = Mark(null, 1) ?? Mark("fallback", 2);
                if (replaced != "fallback" || Trace != 12) return 22;
                Trace = 0;
                string required = Mark("", 1) ?? throw new System.InvalidOperationException("missing");
                if (required != "" || Trace != 1) return 23;
                Trace = 0;
                string assigned = Mark("", 1);
                assigned ??= Mark("fallback", 2);
                if (assigned != "" || Trace != 1) return 24;
                Trace = 0;
                assigned = Mark(null, 1);
                assigned ??= Mark("fallback", 2);
                if (assigned != "fallback" || Trace != 12) return 25;
                string[] targets = new string[1];
                Trace = 0;
                targets[MarkIndex()] ??= Mark("assigned", 2);
                if (targets[0] != "assigned" || Trace != 12) return 26;
                Trace = 0;
                targets[MarkIndex()] ??= Mark("unused", 2);
                if (targets[0] != "assigned" || Trace != 1) return 27;
                Trace = 0;
                string skipped = Mark(null, 1)?.Replace(Mark("t", 2), Mark("T", 3));
                if (skipped != null || Trace != 1) return 28;
                Trace = 0;
                string changed = Mark("text", 1)?.Replace(Mark("t", 2), Mark("T", 3));
                if (changed != "TexT" || Trace != 123) return 29;
                Trace = 0;
                int length = Mark(null, 1)?.Length ?? -1;
                if (length != -1 || Trace != 1) return 30;
                Trace = 0;
                length = Mark("", 1)?.Length ?? -1;
                if (length != 0 || Trace != 1) return 31;
                return 0;
            }
            /// <summary>Checks null patterns and values flowing through generated default storage and return paths.</summary>
            public static int CheckStoragePatterns() {
                string missing = ReturnNull();
                string empty = "";
                if (!(missing is null) || empty is null) return 32;
                if (missing is not null || !(empty is not null)) return 33;
                if (ReturnDefault() != null || DefaultStatic != null) return 34;
                ManagedStringNullFixture instance = new ManagedStringNullFixture();
                if (instance.DefaultProperty != null || instance.NullGetter != null) return 35;
                DefaultStatic = "";
                instance.DefaultProperty = "";
                if (DefaultStatic == null || instance.DefaultProperty == null) return 36;
                DefaultStatic = null;
                instance.DefaultProperty = null;
                if (DefaultStatic != null || instance.DefaultProperty != null) return 37;
                return 0;
            }
            /// <summary>Checks that equal string values preserve the hash contract used by generated dictionaries.</summary>
            public static int CheckHash() {
                System.Collections.Generic.Dictionary<string, int> values = new System.Collections.Generic.Dictionary<string, int>();
                values[""] = 1;
                values[string.Empty] = 2;
                if (values.Count != 1 || values[""] != 2) return 38;
                values["value"] = 3;
                if (values[Concat("val", "ue")] != 3) return 39;
                if ("".GetHashCode() != string.Empty.GetHashCode()) return 40;
                return 0;
            }
            /// <summary>Checks that runtime operations producing no characters still return non-null managed strings.</summary>
            public static int CheckEmptyOperations() {
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                string built = builder.ToString();
                if (built == null || built != "") return 43;
                string concatenated = string.Concat(new string[0]);
                if (concatenated == null || concatenated != "") return 44;
                string joined = string.Join(",", new string[0]);
                if (joined == null || joined != "") return 45;
                string decoded = System.Text.Encoding.UTF8.GetString(new byte[0]);
                if (decoded == null || decoded != "") return 46;
                return 0;
            }
            /// <summary>Dereferences a null receiver so the hosted native harness can verify its exception type.</summary>
            public static int ReadNullLength() {
                string missing = null;
                return missing.Length;
            }
            /// <summary>Evaluates a throwing fallback after recording the null left operand exactly once.</summary>
            public static string ThrowCoalesce() {
                return Mark(null, 1) ?? throw new System.InvalidOperationException("missing");
            }
            /// <summary>Runs literal expected outcomes without referring to the generated string representation.</summary>
            public static int Check() {
                string missing = null;
                string empty = "";
                if (!IsNull(missing)) return 1;
                if (IsNull(empty)) return 2;
                if (IsNotNull(missing)) return 3;
                if (!IsNotNull(empty)) return 4;
                if (!IsNullLeft(missing) || IsNullLeft(empty)) return 5;
                if (IsNotNullLeft(missing) || !IsNotNullLeft(empty)) return 6;
                if (Equal(missing, empty) || Equal(empty, missing)) return 7;
                if (!Equal(missing, missing)) return 8;
                if (string.Empty == null || string.Empty != "") return 9;
                ManagedStringNullFixture instance = new ManagedStringNullFixture();
                if (!IsNull(instance.DefaultField)) return 10;
                string[] values = new string[2];
                if (!IsNull(values[0]) || !IsNull(values[1])) return 11;
                if (Fallback(empty) != "") return 12;
                if (Fallback(missing) != "fallback") return 13;
                if (IsNull(Concat(missing, missing)) || Concat(missing, missing) != "") return 14;
                if (IsNull(Concat(empty, missing)) || Concat(empty, missing) != "") return 15;
                if (Concat(missing, "right") != "right") return 16;
                if (Concat("left", missing) != "left") return 17;
                string interpolated = $"{missing}";
                if (interpolated == null || interpolated != "") return 47;
                if (PlatformSetting(missing, "default") != "default") return 18;
                if (PlatformSetting(empty, "default") != "query:") return 19;
                if (PlatformSetting("value", "default") != "query:value") return 20;
                return 0;
            }
        }
        """;

    /// <summary>Supplies standard storage behind the custom-provider contract so the regression can execute on the host compiler.</summary>
    const string ProviderSource = """
        #pragma once
        #include <cstdlib>
        #include <functional>
        #include <string>
        #include <unordered_map>
        #include <unordered_set>
        #include <vector>
        #define HE_CPP_RUNTIME_HAS_STD_ABORT 1
        namespace he_cpp_custom {
            using String = std::string;
            template<class T> using Vector = std::vector<T>;
            template<class K, class V, class H, class E> using UnorderedMap = std::unordered_map<K, V, H, E>;
            template<class T, class H, class E> using UnorderedSet = std::unordered_set<T, H, E>;
            template<class T> using Hash = std::hash<T>;
            /// <summary>Terminates the host process if the fatal runtime unexpectedly reports a failure.</summary>
            [[noreturn]] inline void Fail(const char*) { std::abort(); }
        }
        """;

    /// <summary>Checks managed null semantics in exception-enabled hosted execution, hosted fatal mode, and provider-backed retro mode.</summary>
    /// <param name="useExceptions">Selects the hosted exception policy while keeping the observable C# behavior identical.</param>
    /// <param name="useCustomRetro">Selects the custom-provider string and collection storage contract.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ConvertedStrings_PreserveNullAndEmptyBehavior(bool useExceptions, bool useCustomRetro) {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "builds", "csharpcodegen", "nullable-strings"));
        string fixtureRoot = Path.Combine(artifactRoot, useCustomRetro ? "retro" : useExceptions ? "hosted" : "fatal");
        string outputRoot = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), FixtureSource);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        if (useCustomRetro) {
            options.RuntimeProfile = CPPRuntimeProfile.CreateCustomRetro();
            options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                [CPPCodegenOptionNames.RuntimeProviderHeader] = "nullable_string_provider.hpp"
            };
        }
        options.RuntimeProfile.UseExceptions = useExceptions;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputRoot);
        Assert.False(converter.Report.HasErrors);
        if (useCustomRetro) {
            File.WriteAllText(Path.Combine(outputRoot, "nullable_string_provider.hpp"), ProviderSource);
        }
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(outputRoot).Run("""
            int result = ManagedStringNullFixture::Check();
            if (result == 0) result = ManagedStringNullFixture::CheckLazy();
            if (result == 0) result = ManagedStringNullFixture::CheckStoragePatterns();
            if (result == 0) result = ManagedStringNullFixture::CheckHash();
            if (result == 0) result = ManagedStringNullFixture::CheckEmptyOperations();
            #if HE_CPP_USE_EXCEPTIONS
            if (result == 0) {
                bool caught = false;
                try { ManagedStringNullFixture::ReadNullLength(); }
                catch (const NullReferenceException&) { caught = true; }
                if (!caught) result = 41;
            }
            if (result == 0) {
                ManagedStringNullFixture::Trace = 0;
                bool caught = false;
                try { ManagedStringNullFixture::ThrowCoalesce(); }
                catch (const InvalidOperationException&) { caught = true; }
                catch (const InvalidOperationException* exception) { caught = true; delete exception; }
                if (!caught || ManagedStringNullFixture::Trace != 1) result = 42;
            }
            #endif
            if (result != 0) std::cerr << "Managed string null semantics failed at check " << result << std::endl;
            return result;
            """);
        Assert.True(result.CompilerExitCode == 0, $"Native compiler exited with {result.CompilerExitCode}: {result.CompilerOutput}");
        Assert.True(result.ProgramExitCode == 0, $"Native program exited with {result.ProgramExitCode}: {result.ProgramOutput}");
    }
}
