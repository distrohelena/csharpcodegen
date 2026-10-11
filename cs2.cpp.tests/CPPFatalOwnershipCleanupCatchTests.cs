namespace cs2.cpp.tests;

/// <summary>
/// Exercises the fatal-profile exception gate with real project conversion and native fixtures.
/// </summary>
public sealed class CPPFatalOwnershipCleanupCatchTests {
    /// <summary>
    /// Keeps cleanup/rethrow handlers in exception builds and elides them only in fatal builds.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteOutput_OwnershipCleanupRethrow_PreservesSelectedFailureModel(bool exceptions) {
        string output = ConvertFixture(exceptions ? "exceptions" : "fatal", exceptions,
            "catch { helengine.NativeOwnership.DisposeAndRelease(ref asset); throw; }");
        string source = File.ReadAllText(Path.Combine(output, "CleanupFixture.cpp"));
        Assert.Equal(exceptions, source.Contains("catch (...)"));
        Assert.Contains("he_cpp_make_scope_exit", source);
        Assert.Equal(exceptions, source.Contains("->Dispose()"));
        File.WriteAllText(Path.Combine(output, "native-harness.cpp"), """
            #include "CleanupFixture.hpp"
            #include "CleanupValue.hpp"
            #include <cassert>
            #include <csignal>
            #include <sys/wait.h>
            #include <unistd.h>
            int main() {
                assert(CleanupFixture::Run(false) == 7);
                assert(CleanupFixture::ReleaseArray(false) == 1);
                assert(CleanupFixture::FinallyCount == 1);
                CleanupFixture::FinallyCount = 0;
                CleanupValue::DisposeCount = 0;
            #if HE_CPP_USE_EXCEPTIONS
                bool caught = false;
                try { CleanupFixture::Run(true); } catch (...) { caught = true; }
                assert(caught);
                assert(CleanupValue::DisposeCount == 1);
                assert(CleanupFixture::FinallyCount == 1);
            #else
                pid_t child = fork();
                assert(child >= 0);
                if (child == 0) {
                    signal(SIGABRT, [](int) {
                        if (CleanupValue::DisposeCount != 0 || CleanupFixture::FinallyCount != 0) _exit(98);
                        signal(SIGABRT, SIG_DFL);
                        raise(SIGABRT);
                    });
                    CleanupFixture::Run(true);
                    _exit(99);
                }
                int status = 0;
                assert(waitpid(child, &status, 0) == child);
                assert(WIFSIGNALED(status) && WTERMSIG(status) == SIGABRT);
            #endif
                return 0;
            }
            """);
    }

    /// <summary>
    /// Refuses recovery, typed/filter catches, side effects and similarly named noncanonical helpers.
    /// </summary>
    [Theory]
    [InlineData("recovery", "catch { return 3; }")]
    [InlineData("typed", "catch (Exception) { helengine.NativeOwnership.DisposeAndRelease(ref asset); throw; }")]
    [InlineData("filtered", "catch when (fail) { helengine.NativeOwnership.DisposeAndRelease(ref asset); throw; }")]
    [InlineData("impostor", "catch { Other.Ownership.DisposeAndRelease(ref asset); throw; }")]
    public void WriteOutput_UnsupportedCatch_RemainsCapabilityError(string name, string handler) {
        Exception error = Assert.ThrowsAny<Exception>(() => ConvertFixture(name, false, handler));
        Assert.Contains("CPP1001", error.ToString());
        Assert.Contains("Exception recovery", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a bounded fixture using the actual ownership helper project rather than a name-only substitute.
    /// </summary>
    /// <param name="name">Stable fixture directory name.</param>
    /// <param name="exceptions">Whether generated C++ supports exception recovery.</param>
    /// <param name="handler">Catch clause being classified by the production converter.</param>
    /// <returns>The generated directory when conversion succeeds.</returns>
    static string ConvertFixture(string name, bool exceptions, string handler) {
        string root = Environment.GetEnvironmentVariable("CSHARPCODEGEN_TEST_ARTIFACT_ROOT")
            ?? "C:/dev/helworks/builds/helengine-xbox/texture-formats/fatal-catch-fixtures";
        string fixture = Path.Combine(root, name);
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
              <ItemGroup><ProjectReference Include="C:/dev/helworks/helengine/engine/helengine.nativeownership/helengine.nativeownership.csproj" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixture, "Fixture.cs"), """
            public class CleanupValue : IDisposable {
                public static int DisposeCount;
                public void Dispose() { DisposeCount++; }
            }
            public class CleanupFixture {
                public static int FinallyCount;
                public static int Run(bool fail) {
                    CleanupValue asset = new CleanupValue();
                    try { if (fail) throw new InvalidOperationException("fatal"); return 7; }
            """ + handler + """
                    finally { FinallyCount++; }
                }
                public static int ReleaseArray(bool fail) {
                    byte[] rgba = new byte[1];
                    try { if (fail) throw new InvalidOperationException("fatal"); return rgba.Length; }
                    catch { helengine.NativeOwnership.Release(ref rgba); throw; }
                }
            }
            namespace Other {
                public static class Ownership {
                    public static void DisposeAndRelease<T>(ref T value) where T : class { value = null; }
                }
            }
            """);
        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        options.CollectDiagnostics = false;
        options.PlatformOptionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            [CPPCodegenOptionNames.UseExceptions] = exceptions ? "true" : "false"
        };
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(Path.Combine(fixture, "Fixture.csproj"));
        string output = Path.Combine(fixture, "generated");
        converter.WriteOutput(output);
        return output;
    }
}
