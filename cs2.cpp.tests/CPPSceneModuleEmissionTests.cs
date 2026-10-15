using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Exercises separately compiled template consumers and source-owned object/callback lifetime emission for native scene modules.</summary>
public sealed class CPPSceneModuleEmissionTests {
    /// <summary>Converts the complete managed fixture with opt-in module flags and verifies that default conversions keep their original ABI.</summary>
    /// <param name="modules">Whether code module templates and lifetime protection are requested.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOutput_SceneModulePolicy_EmitsStableOwnersAndVisibleTemplates(bool modules) {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-ps1/menu-20261007/scene-modules/buildgraph/codegen-tests";
        string fixture = Path.Combine(root, modules ? "modules" : "legacy");
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "Fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(fixture, "Fixture.cs"), """
            using System;
            public class ModuleObject {
                public int Value;
                public ModuleObject() : this(7) { }
                public ModuleObject(int value) { Value = value; }
                public T Echo<T>(T value) { return value; }
                public Action Callback;
                public void SetCallback() { Callback = () => { }; }
                public static event Action Changed;
                public void Handler() { Value++; }
                public void Attach() { Changed += Handler; }
                public void Detach() { Changed -= Handler; }
            }
            public class TemplateBox<T> {
                public T Value;
                public TemplateBox(T value) { Value = value; }
                public T Read() { return Value; }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PlatformOptionValues = new Dictionary<string, string> {
            ["scene-code-modules"] = modules.ToString(),
            ["codegen-header-template-definitions"] = modules.ToString(),
            ["codegen-module-lifetime-pins"] = modules.ToString()
        };
        CPPCodeConverter converter = new(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixture, "Fixture.csproj"));
        string output = Path.Combine(fixture, "generated");
        converter.WriteOutput(output);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean(), report.RootElement.GetRawText());
        string header = File.ReadAllText(Path.Combine(output, "ModuleObject.hpp"));
        string source = File.ReadAllText(Path.Combine(output, "ModuleObject.cpp"));
        string genericHeader = File.ReadAllText(Path.Combine(output, "TemplateBox_1.hpp"));
        if (modules) {
            Assert.Contains("ModuleObject::Echo", header);
            Assert.DoesNotContain("ModuleObject::Echo", source);
            Assert.Contains("TemplateBox_1<T>::Read", genericHeader);
            Assert.Contains("HeCppRetainCodeModule(HELENGINE_CODE_MODULE_ID)", source);
            Assert.Equal(1, source.Split("HeCppRetainCodeModule(HELENGINE_CODE_MODULE_ID)").Length - 1);
            Assert.Contains("ModuleObject::~ModuleObject()", source);
            Assert.Contains("Event::BindPinned(HELENGINE_CODE_MODULE_ID", source);
            Assert.Contains("HeCppPinCodeModuleCallable(HELENGINE_CODE_MODULE_ID", source);
            Assert.DoesNotContain("HeCppRetainCodeModule", genericHeader);
        } else {
            Assert.DoesNotContain("HeCppRetainCodeModule", source);
            Assert.DoesNotContain("ModuleObject::Echo", header);
            Assert.Contains("ModuleObject::Echo", source);
        }
        File.WriteAllText(Path.Combine(output, "native-consumer.cpp"), """
            #include "ModuleObject.hpp"
            #include "TemplateBox_1.hpp"
            #include <cassert>
            #include <cstring>
            static int pins;
            void HeCppRetainCodeModule(const char* id) { if (*id) ++pins; }
            void HeCppReleaseCodeModule(const char* id) { if (*id) --pins; }
            int main() {
                auto* object = new ModuleObject(5);
                assert(pins == 1);
                assert(object->Echo<int32_t>(11) == 11);
                TemplateBox_1<int32_t> box(19);
                assert(box.Read() == 19);
                object->Attach(); assert(pins == 2);
                object->Detach(); assert(pins == 1);
                object->SetCallback(); assert(pins == 2);
                delete object->Callback; object->Callback = nullptr; assert(pins == 1);
                delete object; assert(pins == 0);
            }
            """);
    }
}
