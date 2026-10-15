using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Verifies factory ownership transferred through method parameters into native collections, including deletion before module unload.</summary>
public sealed class CPPSceneModuleOwnedParameterTests {
    /// <summary>Converts owned and borrowed parameter insertions and writes a native destruction-count fixture for Remove, Clear and collection disposal.</summary>
    /// <param name="modules">Whether the converter also protects native code module lifetimes.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOutput_TakesOwnershipParameter_EmitsOwningDictionaryAndListInsertion(bool modules) {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-ps1/menu-20261007/scene-modules/buildgraph/codegen-tests";
        string fixture = Path.Combine(root, "owned-parameter", modules ? "modules" : "legacy");
        Directory.CreateDirectory(fixture);
        string project = Path.Combine(fixture, "Fixture.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(fixture, "Fixture.cs"), """
            using System;
            using System.Collections.Generic;
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute { }
            public interface IItem { void Touch(); }
            public interface IFactory<T> { T Create(); }
            public interface IDisposableItem : IDisposable { void Touch(); }
            public class Item : IItem, IFactory<int>, IDisposableItem {
                public void Touch() { }
                public int Create() { return 7; }
                public void Dispose() { }
            }
            public sealed class Receiver {
                public Dictionary<string, IItem> Map = new Dictionary<string, IItem>();
                public List<IItem> Items = new List<IItem>();
                public Dictionary<string, IFactory<int>> Factories = new Dictionary<string, IFactory<int>>();
                public List<IDisposableItem> DisposableItems = new List<IDisposableItem>();
                public void AdoptDictionary([NativeTakesOwnership] IItem item) { Map.Add("id", item); }
                public void AdoptList([NativeTakesOwnership] IItem item) { Items.Add(item); }
                public void BorrowDictionary(IItem item) { Map.Add("borrow", item); }
                public void BorrowList(IItem item) { Items.Add(item); }
                public void Remove() { Map.Remove("id"); }
                public void Clear() { Items.Clear(); }
                public void AdoptFactory([NativeTakesOwnership] IFactory<int> item) { Factories.Add("factory", item); }
                public void RemoveFactory() { Factories.Remove("factory"); }
                public void AdoptDisposable([NativeTakesOwnership] IDisposableItem item) { DisposableItems.Add(item); }
                public void ClearDisposable() { DisposableItems.Clear(); }
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
        converter.AddCsproj(project);
        string output = Path.Combine(fixture, "generated");
        converter.WriteOutput(output);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean(), report.RootElement.GetRawText());
        string source = File.ReadAllText(Path.Combine(output, "Receiver.cpp"));
        Assert.Contains("->AddOwned(HeCppString(\"id\"), item)", source);
        Assert.Contains("->AddOwned(item)", source);
        Assert.Contains("->Add(HeCppString(\"borrow\"), item)", source);
        Assert.Contains("->Add(item)", source);
        Assert.Contains("virtual ~IItem() = default;", File.ReadAllText(Path.Combine(output, "IItem.hpp")));
        Assert.Contains("virtual ~IFactory_1() = default;", File.ReadAllText(Path.Combine(output, "IFactory_1.hpp")));
        Assert.Contains("virtual ~IDisposableItem() = default;", File.ReadAllText(Path.Combine(output, "IDisposableItem.hpp")));
        File.WriteAllText(Path.Combine(output, "native-consumer.cpp"), """
            #include "Receiver.hpp"
            #include "Item.hpp"
            #include "IItem.hpp"
            #include <cassert>
            static int destroyed;
            #ifdef HE_FIXTURE_MODULES
            static int pins;
            void HeCppRetainCodeModule(const char* id) { if (*id) ++pins; }
            void HeCppReleaseCodeModule(const char* id) { if (*id) --pins; }
            #define EXPECT_PINS(value) assert(pins == value)
            #else
            #define EXPECT_PINS(value) ((void)0)
            #endif
            struct TrackedItem final : Item { ~TrackedItem() override { ++destroyed; } };
            int main() {
              {
                Receiver receiver;
                EXPECT_PINS(1);
                receiver.AdoptDictionary(new TrackedItem());
                receiver.Remove(); assert(destroyed == 1); EXPECT_PINS(1);
                receiver.AdoptList(new TrackedItem());
                receiver.Clear(); assert(destroyed == 2); EXPECT_PINS(1);
                receiver.AdoptDictionary(new TrackedItem());
                delete receiver.Map; receiver.Map = nullptr; assert(destroyed == 3); EXPECT_PINS(1);
                receiver.Map = new Dictionary<std::string, IItem*>();
                delete receiver.Items; receiver.Items = new List<IItem*>();
                auto* borrowed = new TrackedItem();
                receiver.BorrowDictionary(borrowed);
                receiver.Map->Clear(); assert(destroyed == 3);
                receiver.BorrowList(borrowed);
                receiver.Clear(); assert(destroyed == 3);
                delete borrowed; assert(destroyed == 4); EXPECT_PINS(1);
                delete receiver.Map; receiver.Map = nullptr;
                delete receiver.Items; receiver.Items = nullptr;
                receiver.AdoptFactory(new TrackedItem());
                receiver.RemoveFactory(); assert(destroyed == 5); EXPECT_PINS(1);
                receiver.AdoptDisposable(new TrackedItem());
                receiver.ClearDisposable(); assert(destroyed == 6); EXPECT_PINS(1);
                delete receiver.Factories; receiver.Factories = nullptr;
                delete receiver.DisposableItems; receiver.DisposableItems = nullptr;
              }
              EXPECT_PINS(0);
            }
            """);
    }
}
