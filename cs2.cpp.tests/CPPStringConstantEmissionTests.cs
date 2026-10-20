using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Checks string constants consumed by earlier static initializers, including null, empty and generic constant values.</summary>
public sealed class CPPStringConstantEmissionTests {
    /// <summary>Verifies startup dependencies, null/empty values, dependent constants and generic definitions with both ordinary and scene-module emission.</summary>
    /// <param name="headerTemplates">Whether generic class definitions must be visible to separately compiled scene modules.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOutput_StringConstantsPreserveStartupDependencies(bool headerTemplates) {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "cs2cpp-string-constants");
        string fixture = Path.Combine(root, "string-constants", headerTemplates ? "modules" : "ordinary");
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixture, "Fixture.cs"), """
            public class StringConstantsFixture {
                public const int Count = 3;
                public const string Text = "constant-text";
                public const string Empty = "";
                public const string Missing = null;
                public const string Alias = Text;
                public const string Combined = Text + "-suffix";
                public static int get_Text() { return 17; }
                public static bool Verify() {
                    return Text == "constant-text" && Empty != null && Empty == ""
                        && Missing == null && Alias == Text && Combined == "constant-text-suffix";
                }
            }
            public class AStringConstantsStartupConsumer {
                public static readonly string BootText = StringConstantsFixture.Text;
                public static readonly string BootEmpty = StringConstantsFixture.Empty;
                public static readonly string BootMissing = StringConstantsFixture.Missing;
                public static bool Verify() {
                    return BootText == "constant-text" && BootEmpty == "" && BootEmpty != null && BootMissing == null;
                }
            }
            public class StringConstantsConsumer {
                public static bool Verify() { return StringConstantsFixture.Verify(); }
            }
            public class GenericConstants<T> {
                public const string Text = "generic-text";
                public static bool Verify() { return Text == "generic-text"; }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PlatformOptionValues = new Dictionary<string, string> {
            ["codegen-header-template-definitions"] = headerTemplates.ToString()
        };
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixture, "Fixture.csproj"));
        string output = Path.Combine(fixture, "generated");
        converter.WriteOutput(output);
        Assert.False(converter.Report.HasErrors);
        string header = File.ReadAllText(Path.Combine(output, "StringConstantsFixture.hpp"));
        Assert.Contains("inline static const int32_t Count = 3;", header);
        Assert.Contains("static const HeCppString& Text();", header);
        Assert.DoesNotContain("inline static const HeCppString", header);
        string implementation = File.ReadAllText(Path.Combine(output, "StringConstantsFixture.cpp"));
        Assert.Contains("const HeCppString& StringConstantsFixture::Text()", implementation);
        Assert.Contains("static const HeCppString& value = ", implementation);
        string stringRuntime = File.ReadAllText(Path.Combine(output, "runtime", "native_string.hpp"));
        Assert.Contains("const HeCppString& get_Empty()", stringRuntime);
        Assert.DoesNotContain("inline static const HeCppString Empty", stringRuntime);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output).Run("""
            static_assert(std::is_same_v<decltype(StringConstantsFixture::Text()), const HeCppString&>);
            if (&StringConstantsFixture::Text() != &StringConstantsFixture::Text()) return 6;
            if (&String::get_Empty() != &String::get_Empty()) return 7;
            if (String::get_Empty().is_null() || !String::get_Empty().empty()) return 8;
            if (StringConstantsFixture::get_Text() != 17) return 14;
            if (!StringConstantsFixture::Verify()) return 1;
            if (!StringConstantsConsumer::Verify()) return 2;
            if (!AStringConstantsStartupConsumer::Verify()) return 5;
            if (!GenericConstants_1<int32_t>::Verify()) return 3;
            if (!GenericConstants_1<bool>::Verify()) return 4;
            using Storage = HeCppDeferredStaticStorage<TrackedDeferredValue>;
            using Lifetime = HeCppDeferredStaticLifetime<TrackedDeferredValue>;
            static_assert(!std::is_copy_constructible_v<Storage>);
            static_assert(std::is_trivially_destructible_v<Storage>);
            { Storage unused; Lifetime finalizer(unused); }
            if (TrackedDeferredValue::Destroyed != 0) return 9;
            alignas(Storage) unsigned char arena[sizeof(Storage)];
            for (int generation = 1; generation <= 2; ++generation) {
                Storage* storage = new (arena) Storage;
                {
                    Lifetime finalizer(*storage);
                    if (storage->Initialize(37).Value != 37 || TrackedDeferredValue::Live != 1) return 10;
                }
                storage->~Storage();
                if (TrackedDeferredValue::Live != 0 || TrackedDeferredValue::Destroyed != generation) return 11;
            }
            try {
                Storage failed;
                Lifetime finalizer(failed);
                failed.Initialize(-1);
                return 12;
            } catch (int) {
                if (TrackedDeferredValue::Live != 0 || TrackedDeferredValue::Destroyed != 2) return 13;
            }
            """, """
            struct TrackedDeferredValue {
                inline static int Live = 0;
                inline static int Destroyed = 0;
                int Value;
                explicit TrackedDeferredValue(int value) : Value(value) {
                    if (value < 0) throw value;
                    ++Live;
                }
                ~TrackedDeferredValue() { --Live; ++Destroyed; }
            };
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native program exited with {result.ProgramExitCode}: {result.ProgramOutput}");
    }
}
