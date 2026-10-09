using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Converts real computed string getters and declaration-pattern ternaries into executable native lifetime fixtures.</summary>
public sealed class CPPGetterPatternLifetimeTests {
    /// <summary>Preserves references to stored strings while computed strings and pattern-dependent branches remain valid native values.</summary>
    [Fact]
    public void WriteOutput_ComputedStringAndPatternConditional_PreserveLifetimeAndLazyScope() {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "builds", "csharpcodegen", "getter-pattern-lifetime"));
        string fixtureRoot = Path.Combine(artifactRoot, "getter-pattern-lifetime");
        string outputRoot = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            public class StringLifetimeFixture {
                string Backing;
                public string Id { get; private set; }
                public string Stored { get { return Backing; } }
                public string DisplayName { get { return Id.Replace("Gx.", "GX ").Replace(".TLUT.", " / TLUT "); } }
                public void SetId(string value) { Id = value; Backing = value; }
            }
            public interface IProxy { int Value { get; } }
            public class BaseOwner { public virtual int Tag() { return 0; } }
            public class DerivedOwner : BaseOwner, IProxy {
                public int Value { get; }
                public DerivedOwner(int value) { Value = value; }
                public override int Tag() { return 1; }
            }
            public class PatternLifetimeFixture {
                public static int OwnerCalls;
                public static int FallbackCalls;
                static BaseOwner GetOwner(BaseOwner owner) { OwnerCalls++; return owner; }
                static int FailBranch() { FallbackCalls++; return -7; }
                public static int Select(bool guard, BaseOwner owner) {
                    return guard && GetOwner(owner) is DerivedOwner pattern ? pattern.Value : FailBranch();
                }
                public static int SelectProxy(BaseOwner owner) {
                    return GetOwner(owner) is IProxy proxy ? proxy.Value : FailBranch();
                }
                public static int Assigned(bool guard, BaseOwner owner) {
                    int result = guard && GetOwner(owner) is DerivedOwner local ? local.Value : FailBranch();
                    return result;
                }
            }
            public class PropertyPatternFixture {
                public BaseOwner Owner;
                public int SetterValue;
                public int Block {
                    get { return Owner is DerivedOwner pattern ? pattern.Value : -7; }
                    set { SetterValue = Owner is DerivedOwner assigned ? assigned.Value + value : -7; }
                }
                public int Arrow => Owner is DerivedOwner arrow ? arrow.Value : -7;
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.RuntimeProfile.UseExceptions = true;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixtureRoot, "Fixture.csproj"));
        converter.WriteOutput(outputRoot);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
        string header = File.ReadAllText(Path.Combine(outputRoot, "StringLifetimeFixture.hpp"));
        Assert.Contains("std::string get_DisplayName();", header);
        Assert.DoesNotContain("const std::string& get_DisplayName();", header);
        Assert.Contains("const std::string& get_Id();", header);
        Assert.Contains("const std::string& get_Stored();", header);
        string propertySource = File.ReadAllText(Path.Combine(outputRoot, "PropertyPatternFixture.cpp"));
        Assert.Contains("#include \"DerivedOwner.hpp\"", propertySource);
        File.WriteAllText(Path.Combine(outputRoot, "native-harness.cpp"), """
            #include "StringLifetimeFixture.hpp"
            #include "PatternLifetimeFixture.hpp"
            #include "DerivedOwner.hpp"
            #include "BaseOwner.hpp"
            #include "PropertyPatternFixture.hpp"
            #include <cassert>
            #include <string>
            #include <type_traits>
            #include <utility>
            static_assert(std::is_same_v<decltype(std::declval<StringLifetimeFixture&>().get_DisplayName()), std::string>);
            static_assert(std::is_same_v<decltype(std::declval<StringLifetimeFixture&>().get_Id()), const std::string&>);
            static_assert(std::is_same_v<decltype(std::declval<StringLifetimeFixture&>().get_Stored()), const std::string&>);
            int main() {
                StringLifetimeFixture strings;
                strings.SetId("Gx.CI8.TLUT.IA8");
                const std::string* idAddress = &strings.get_Id();
                const std::string* storedAddress = &strings.get_Stored();
                std::string first = strings.get_DisplayName();
                strings.SetId("Gx.CI4.TLUT.RGB565");
                std::string second = strings.get_DisplayName();
                assert(first == "GX CI8 / TLUT IA8" && second == "GX CI4 / TLUT RGB565");
                assert(idAddress == &strings.get_Id() && storedAddress == &strings.get_Stored());
                assert(*idAddress == "Gx.CI4.TLUT.RGB565");
                DerivedOwner valid(73); BaseOwner invalid;
                PatternLifetimeFixture::OwnerCalls = 0; PatternLifetimeFixture::FallbackCalls = 0;
                assert(PatternLifetimeFixture::Select(false, &valid) == -7);
                assert(PatternLifetimeFixture::OwnerCalls == 0 && PatternLifetimeFixture::FallbackCalls == 1);
                PatternLifetimeFixture::OwnerCalls = 0; PatternLifetimeFixture::FallbackCalls = 0;
                assert(PatternLifetimeFixture::Select(true, &valid) == 73);
                assert(PatternLifetimeFixture::OwnerCalls == 1 && PatternLifetimeFixture::FallbackCalls == 0);
                PatternLifetimeFixture::OwnerCalls = 0; PatternLifetimeFixture::FallbackCalls = 0;
                assert(PatternLifetimeFixture::Select(true, &invalid) == -7);
                assert(PatternLifetimeFixture::OwnerCalls == 1 && PatternLifetimeFixture::FallbackCalls == 1);
                PatternLifetimeFixture::OwnerCalls = 0; PatternLifetimeFixture::FallbackCalls = 0;
                assert(PatternLifetimeFixture::Select(true, nullptr) == -7);
                assert(PatternLifetimeFixture::OwnerCalls == 1 && PatternLifetimeFixture::FallbackCalls == 1);
                PatternLifetimeFixture::OwnerCalls = 0; PatternLifetimeFixture::FallbackCalls = 0;
                assert(PatternLifetimeFixture::SelectProxy(&valid) == 73);
                assert(PatternLifetimeFixture::OwnerCalls == 1 && PatternLifetimeFixture::FallbackCalls == 0);
                PatternLifetimeFixture::OwnerCalls = 0; PatternLifetimeFixture::FallbackCalls = 0;
                assert(PatternLifetimeFixture::SelectProxy(&invalid) == -7);
                assert(PatternLifetimeFixture::OwnerCalls == 1 && PatternLifetimeFixture::FallbackCalls == 1);
                PatternLifetimeFixture::OwnerCalls = 0; PatternLifetimeFixture::FallbackCalls = 0;
                assert(PatternLifetimeFixture::Assigned(true, &valid) == 73);
                assert(PatternLifetimeFixture::OwnerCalls == 1 && PatternLifetimeFixture::FallbackCalls == 0);
                PatternLifetimeFixture::OwnerCalls = 0; PatternLifetimeFixture::FallbackCalls = 0;
                assert(PatternLifetimeFixture::Assigned(false, &valid) == -7);
                assert(PatternLifetimeFixture::OwnerCalls == 0 && PatternLifetimeFixture::FallbackCalls == 1);
                PropertyPatternFixture properties;
                properties.Owner = &valid;
                assert(properties.get_Block() == 73 && properties.get_Arrow() == 73);
                properties.set_Block(5); assert(properties.SetterValue == 78);
                properties.Owner = &invalid;
                assert(properties.get_Block() == -7 && properties.get_Arrow() == -7);
                properties.set_Block(5); assert(properties.SetterValue == -7);
                properties.Owner = nullptr;
                assert(properties.get_Block() == -7 && properties.get_Arrow() == -7);
                properties.set_Block(5); assert(properties.SetterValue == -7);
            }
            """);
    }
}
