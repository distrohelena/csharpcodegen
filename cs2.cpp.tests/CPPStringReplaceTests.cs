using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies native replace-all lowering, including chained calls and C# receiver/argument ordering.
/// </summary>
public class CPPStringReplaceTests {
    /// <summary>
    /// Generates real Replace overloads and a C++ executable harness that checks replacement and evaluation semantics.
    /// </summary>
    [Fact]
    public void WriteOutput_StringReplace_UsesSequencedNativeHelper() {
        string artifactRoot = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "builds", "csharpcodegen", "string-replace"));
        string fixtureRoot = Path.Combine(artifactRoot, "string-replace");
        string outputRoot = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), """
            public class StringReplaceFixture {
                public static int Calls;
                public static int Order;
                static string Receiver() { Calls++; Order = Order * 10 + 1; return "Xbox.A.Xbox.B"; }
                static string Old() { Calls++; Order = Order * 10 + 2; return "Xbox."; }
                static string New() { Calls++; Order = Order * 10 + 3; return "Xbox "; }
                public static string Chained() { return Receiver().Replace(Old(), New()).Replace(".", " "); }
                public static string Named() { return Receiver().Replace(newValue: New(), oldValue: Old()); }
                public static string Characters(string value) { return value.Replace('.', ' '); }
                public static string Transform(string value, string oldValue, string newValue) { return value.Replace(oldValue, newValue); }
                public string DisplayName;
                public StringReplaceFixture(string id) { DisplayName = id.Replace("Xbox.", "Xbox ").Replace(".", " "); }
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
        string output = File.ReadAllText(Path.Combine(outputRoot, "StringReplaceFixture.cpp"));
        Assert.Contains("String::Replace(", output);
        Assert.Contains("__replace_receiver_", output);
        Assert.DoesNotContain(".Replace(", output);
        File.WriteAllText(Path.Combine(fixtureRoot, "runtime-harness.cpp"), """
            #include "StringReplaceFixture.hpp"
            #include "runtime/native_exceptions.hpp"
            #include <cassert>
            int main() {
                StringReplaceFixture::Calls = 0; StringReplaceFixture::Order = 0;
                assert(StringReplaceFixture::Chained() == "Xbox A Xbox B");
                assert(StringReplaceFixture::Calls == 3 && StringReplaceFixture::Order == 123);
                StringReplaceFixture::Calls = 0; StringReplaceFixture::Order = 0;
                assert(StringReplaceFixture::Named() == "Xbox A.Xbox B");
                assert(StringReplaceFixture::Calls == 3 && StringReplaceFixture::Order == 132);
                assert(StringReplaceFixture::Characters("a.b.c") == "a b c");
                assert(StringReplaceFixture::Transform("aaa", "aa", "b") == "ba");
                assert(StringReplaceFixture::Transform("aaa", "a", "aa") == "aaaaaa");
                assert(StringReplaceFixture::Transform("aaa", "a", "") == "");
                assert(StringReplaceFixture::Transform("unchanged", "missing", "x") == "unchanged");
                StringReplaceFixture instance("Xbox.Linear.A8R8G8B8");
                assert(instance.DisplayName == "Xbox Linear A8R8G8B8");
                bool invalid = false;
                try { StringReplaceFixture::Transform("input", "", "x"); }
                catch (const ArgumentException&) { invalid = true; }
                assert(invalid);
            }
            """);
    }
}
