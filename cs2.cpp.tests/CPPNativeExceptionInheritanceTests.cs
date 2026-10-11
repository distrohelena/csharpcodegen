using System.Text.Json;

namespace cs2.cpp.tests;

/// <summary>Verifies generated exception subclasses retain their concrete identity while framework bases resolve to the native runtime.</summary>
public sealed class CPPNativeExceptionInheritanceTests {
    /// <summary>Converts real exception inheritance in recoverable and fatal profiles without importing framework serialization interfaces.</summary>
    /// <param name="exceptions">Selects recoverable native exceptions instead of the fatal failure policy.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteOutput_NativeExceptionBase_PreservesGeneratedSubclassAndDeclaredInterfaces(bool exceptions) {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-ps1/menu-20261007/texturedraytracing/exception-inheritance";
        string fixture = Path.Combine(root, exceptions ? "exceptions" : "fatal");
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixture, "Fixture.cs"), """
            public interface ICapacityError { int GetRequested(); }
            public class CapacityError : System.InvalidOperationException, ICapacityError {
                public int Requested;
                public CapacityError(string message, int requested) : base(message) { Requested = requested; }
                public int GetRequested() { return Requested; }
            }
            public class NestedCapacityError : CapacityError {
                public NestedCapacityError(string message, int requested) : base(message, requested) { }
            }
            namespace UserTypes {
                public class CustomBaseError : System.Exception {
                    public CustomBaseError(string message) : base(message) { }
                }
            }
            public class UserError : UserTypes.CustomBaseError {
                public UserError(string message) : base(message) { }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.WriteConversionReport = true;
        options.PlatformOptionValues = new Dictionary<string, string> {
            [CPPCodegenOptionNames.UseExceptions] = exceptions ? "true" : "false"
        };
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixture, "Fixture.csproj"));
        string output = Path.Combine(fixture, "generated");
        converter.WriteOutput(output);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "cpp-conversion-report.json")));
        Assert.False(report.RootElement.GetProperty("hasErrors").GetBoolean());
        string capacityHeader = File.ReadAllText(Path.Combine(output, "CapacityError.hpp"));
        Assert.Contains("#include \"runtime/native_exceptions.hpp\"", capacityHeader);
        Assert.DoesNotContain("#include \"InvalidOperationException.hpp\"", capacityHeader);
        Assert.DoesNotContain("iserializable.hpp", capacityHeader);
        Assert.Contains("#include \"ICapacityError.hpp\"", capacityHeader);
        Assert.Contains("public ::InvalidOperationException", capacityHeader);
        string nestedHeader = File.ReadAllText(Path.Combine(output, "NestedCapacityError.hpp"));
        Assert.Contains("#include \"CapacityError.hpp\"", nestedHeader);
        Assert.DoesNotContain("iserializable.hpp", nestedHeader);
        string userHeader = File.ReadAllText(Path.Combine(output, "UserError.hpp"));
        Assert.Contains("#include \"CustomBaseError.hpp\"", userHeader);
        File.WriteAllText(Path.Combine(output, "native-harness.cpp"), """
            #include "NestedCapacityError.hpp"
            #include "runtime/native_exceptions.hpp"
            #include <cassert>
            #include <string>
            #include <type_traits>
            static_assert(std::is_base_of_v<InvalidOperationException, CapacityError>);
            static_assert(std::is_base_of_v<ICapacityError, NestedCapacityError>);
            int main() {
                NestedCapacityError error("capacity exhausted", 73);
                ICapacityError* declaredInterface = &error;
                assert(declaredInterface->GetRequested() == 73);
                assert(std::string(error.what()) == "capacity exhausted");
            #if HE_CPP_USE_EXCEPTIONS
                bool caught = false;
                try { throw new NestedCapacityError("typed failure", 19); }
                catch (InvalidOperationException* failure) {
                    auto* concrete = dynamic_cast<NestedCapacityError*>(failure);
                    assert(concrete != nullptr && concrete->GetRequested() == 19);
                    delete failure;
                    caught = true;
                }
                assert(caught);
            #endif
            }
            """);
    }
}
