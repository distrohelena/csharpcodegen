namespace cs2.cpp.tests;

/// <summary>
/// Checks generated header dependencies when generic inheritance comes from a separate referenced project.
/// </summary>
public sealed class CPPExternalGenericInheritanceHeaderTests {
    /// <summary>
    /// Preserves external base/interface arity and uses only headers produced by their own conversion.
    /// </summary>
    [Fact]
    public void WriteOutput_ExternalGenericInheritance_UsesEmittedHeaderNames() {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-xbox/texture-formats/external-generic-fixture";
        string library = Path.Combine(root, "library");
        string consumer = Path.Combine(root, "consumer");
        string output = Path.Combine(root, "generated");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(consumer);
        File.WriteAllText(Path.Combine(library, "Library.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>
            """);
        File.WriteAllText(Path.Combine(library, "Library.cs"), """
            namespace External {
                public interface GenericContract<T> { int Count(); }
                public abstract class GenericBase<T> { public virtual int Count() { return 7; } }
            }
            """);
        File.WriteAllText(Path.Combine(consumer, "Consumer.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
            """ + "<ItemGroup><ProjectReference Include=\"" + Path.Combine(library, "Library.csproj") + "\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(consumer, "Consumer.cs"), """
            public class GenericConsumer : External.GenericBase<int>, External.GenericContract<int> {
                public override int Count() { return 9; }
            }
            namespace Consumers {
                public sealed class EquatableConsumer : IEquatable<EquatableConsumer> {
                    public bool Equals(EquatableConsumer other) { return ReferenceEquals(this, other); }
                }
            }
            """);
        ConvertProject(Path.Combine(library, "Library.csproj"), output);
        ConvertProject(Path.Combine(consumer, "Consumer.csproj"), output);
        string header = File.ReadAllText(Path.Combine(output, "GenericConsumer.hpp"));
        Assert.Contains("#include \"GenericBase_1.hpp\"", header);
        Assert.Contains("#include \"GenericContract_1.hpp\"", header);
        Assert.DoesNotContain("#include \"GenericBase.hpp\"", header);
        Assert.DoesNotContain("#include \"GenericContract.hpp\"", header);
        Assert.True(File.Exists(Path.Combine(output, "GenericBase_1.hpp")));
        Assert.True(File.Exists(Path.Combine(output, "GenericContract_1.hpp")));
        string equatableHeader = File.ReadAllText(Path.Combine(output, "EquatableConsumer.hpp"));
        Assert.Contains("#include \"runtime/native_equatable.hpp\"", equatableHeader);
        Assert.DoesNotContain("#include \"IEquatable_1.hpp\"", equatableHeader);
        File.WriteAllText(Path.Combine(root, "native-harness.cpp"), """
            #include "GenericConsumer.hpp"
            #include "GenericBase_1.cpp"
            #include "EquatableConsumer.hpp"
            #include <cassert>
            int main() {
                GenericConsumer value;
                assert(value.Count() == 9);
                EquatableConsumer same;
                EquatableConsumer different;
                assert(same.Equals(&same));
                assert(!same.Equals(&different));
            }
            """);
    }

    /// <summary>
    /// Converts one real project while leaving dependencies to their separately generated headers.
    /// </summary>
    /// <param name="project">Project whose declared classes are converted.</param>
    /// <param name="output">Shared generated directory used by both project conversions.</param>
    static void ConvertProject(string project, string output) {
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(project);
        converter.WriteOutput(output);
    }
}
