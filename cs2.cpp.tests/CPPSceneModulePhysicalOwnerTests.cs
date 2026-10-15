using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Exercises real generated nongeneric classes with method templates across two physical code-module owners.</summary>
public sealed class CPPSceneModulePhysicalOwnerTests {
    /// <summary>Emits independent source-owned constructors, destructors and escaped callbacks while header templates remain independent of the consuming module.</summary>
    [Fact]
    public void WriteOutput_HeaderTemplatesPreservePhysicalObjectAndCallbackOwners() {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-ps1/menu-20261007/scene-modules/kernel-policy-tests";
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "PhysicalLeft.cs"), """
            using System;
            public class PhysicalLeft {
                public int Value;
                public PhysicalLeft(int value) { Value = value; }
                public T Echo<T>(T value) { return value; }
                public static Action Escaped;
                public static event Action Changed;
                public void Handler() { Value++; }
                public void Attach() { Changed += Handler; }
                public void Detach() { Changed -= Handler; }
                public static void InstallCallback() { Escaped = () => { }; }
            }
            """);
        File.WriteAllText(Path.Combine(root, "PhysicalRight.cs"), """
            public class PhysicalRight {
                public PhysicalRight() { }
                public T Echo<T>(T value) { return value; }
                public int ReadLeft(PhysicalLeft value) { return value.Echo<int>(73); }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PlatformOptionValues = new Dictionary<string, string> {
            ["scene-code-modules"] = "true",
            ["codegen-header-template-definitions"] = "true",
            ["codegen-module-lifetime-pins"] = "true"
        };
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(root, "Fixture.csproj"));
        string output = Path.Combine(root, "generated");
        converter.WriteOutput(output);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean(), report.RootElement.GetRawText());
        foreach (string type in new[] { "PhysicalLeft", "PhysicalRight" }) {
            string header = File.ReadAllText(Path.Combine(output, type + ".hpp"));
            string source = File.ReadAllText(Path.Combine(output, type + ".cpp"));
            Assert.Contains(type + "::Echo", header);
            Assert.DoesNotContain("HELENGINE_CODE_MODULE_ID", header);
            Assert.Contains("HeCppRetainCodeModule(HELENGINE_CODE_MODULE_ID)", source);
            Assert.Contains("HeCppReleaseCodeModule(HELENGINE_CODE_MODULE_ID)", source);
            Assert.DoesNotContain(type + "::Echo", source);
        }
        string leftSource = File.ReadAllText(Path.Combine(output, "PhysicalLeft.cpp"));
        Assert.Contains("Event::BindPinned(HELENGINE_CODE_MODULE_ID", leftSource);
        Assert.Contains("HeCppPinCodeModuleCallable(HELENGINE_CODE_MODULE_ID", leftSource);
        File.WriteAllText(Path.Combine(output, "native-consumer.cpp"), """
            #include "PhysicalLeft.hpp"
            #include "PhysicalRight.hpp"
            #include <cassert>
            #include <cstring>
            static int leftPins;
            static int rightPins;
            void HeCppRetainCodeModule(const char* id) {
                if (!std::strcmp(id, "left")) ++leftPins;
                else if (!std::strcmp(id, "right")) ++rightPins;
                else assert(!*id);
            }
            void HeCppReleaseCodeModule(const char* id) {
                if (!std::strcmp(id, "left")) { assert(leftPins > 0); --leftPins; }
                else if (!std::strcmp(id, "right")) { assert(rightPins > 0); --rightPins; }
                else assert(!*id);
            }
            int main() {
                auto* left = new PhysicalLeft(11);
                auto* right = new PhysicalRight();
                assert(leftPins == 1 && rightPins == 1);
                assert(left->Echo<int32_t>(19) == 19);
                assert(right->Echo<int32_t>(23) == 23);
                assert(right->ReadLeft(left) == 73);
                assert(leftPins == 1 && rightPins == 1);
                left->Attach(); assert(leftPins == 2 && rightPins == 1);
                left->Detach(); assert(leftPins == 1 && rightPins == 1);
                PhysicalLeft::InstallCallback(); assert(leftPins == 2 && rightPins == 1);
                delete left; assert(leftPins == 1 && rightPins == 1);
                (*PhysicalLeft::Escaped)(); assert(leftPins == 1 && rightPins == 1);
                delete right; assert(leftPins == 1 && rightPins == 0);
                delete PhysicalLeft::Escaped; PhysicalLeft::Escaped = nullptr;
                assert(leftPins == 0 && rightPins == 0);
            }
            """);
    }
}
