using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Compiles and runs generated C++ that calls real Windows APIs, and compares each result with the same C# run as managed P/Invoke.
/// </summary>
[Trait("Category", "NativeCompile")]
public sealed class CPPPInvokeNativeExecutionTests {
    /// <summary>
    /// Converts the probe, runs it natively, and returns the printed value together with the managed value.
    /// </summary>
    static void AssertNativeMatchesManaged(string source, string prelude, Func<long, bool> sanity) {
        var output = CPPCompileValidationRegressionTests.RunConversion(source, allowUnsafe: true);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("std::cout << static_cast<long long>(Probe::Run());", prelude);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.Equal(0, result.ProgramExitCode);
        long native = long.Parse(result.ProgramOutput.Trim().Split(Environment.NewLine)[0]);
        long managed = CPPManagedSnippetRunner.Run(source, "Probe", "Run");
        Assert.True(sanity(native), $"native value {native} failed the sanity check");
        Assert.Equal(managed, native);
    }

    /// <summary>
    /// Ensures the simplest import compiles, links against kernel32 and returns the same success flag in both modes.
    /// </summary>
    [Fact]
    public void GetTickCount64_CompilesLinksAndMatchesManaged() {
        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public static class Probe {
                [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
                public static long Run() { return GetTickCount64() > 0 ? 1 : 0; }
            }
            """, string.Empty, value => value == 1);
    }

    /// <summary>
    /// Ensures a by-ref struct import compiles in a unity build that already includes Windows.h and matches the managed result.
    /// </summary>
    [Fact]
    public void GetCursorPos_WithWindowsHeaderInUnity_CompilesAndMatchesManaged() {
        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public struct NativePoint { public int X; public int Y; }
            public static class Probe {
                [DllImport("user32.dll")] static extern int GetCursorPos(out NativePoint point);
                [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
                public static long Run() {
                    int ok = GetCursorPos(out NativePoint point);
                    nint window = WindowFromPoint(point);
                    return ok != 0 ? 1 : 0;
                }
            }
            """, "#include <Windows.h>", value => value == 0 || value == 1);
    }

    /// <summary>
    /// Ensures a stdcall callback round trip enumerates the same number of locales natively and managed, with lParam intact.
    /// </summary>
    [Fact]
    public void EnumSystemLocalesEx_CallbackRoundTrip_MatchesManaged() {
        AssertNativeMatchesManaged("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            public static unsafe class Probe {
                static long Count;
                static long BadParameter;
                [DllImport("kernel32.dll")] static extern int EnumSystemLocalesEx(delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback, uint flags, nint lParam, nint reserved);
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                static int OnLocale(ushort* name, uint flags, nint lParam) {
                    if (lParam != 42) { BadParameter++; }
                    Count++;
                    return 1;
                }
                public static long Run() {
                    Count = 0;
                    BadParameter = 0;
                    EnumSystemLocalesEx(&OnLocale, 0, 42, 0);
                    return BadParameter == 0 ? Count : -1;
                }
            }
            """, string.Empty, value => value > 0);
    }

    /// <summary>
    /// Ensures a callback declared in one class can have its address taken in another class and passed to a native
    /// enumerator, so the calling class's translation unit sees the declaring class's trampoline, and that the
    /// native enumeration count matches the managed one.
    /// </summary>
    [Fact]
    public void EnumSystemLocalesEx_CallbackDeclaredInAnotherClass_MatchesManaged() {
        AssertNativeMatchesManaged("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            public static unsafe class LocaleCallbacks {
                public static long Count;
                public static long BadParameter;
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                public static int OnLocale(ushort* name, uint flags, nint lParam) {
                    if (lParam != 7) { BadParameter++; }
                    Count++;
                    return 1;
                }
            }
            public static unsafe class Probe {
                [DllImport("kernel32.dll")] static extern int EnumSystemLocalesEx(delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback, uint flags, nint lParam, nint reserved);
                public static long Run() {
                    LocaleCallbacks.Count = 0;
                    LocaleCallbacks.BadParameter = 0;
                    EnumSystemLocalesEx(&LocaleCallbacks.OnLocale, 0, 7, 0);
                    return LocaleCallbacks.BadParameter == 0 ? LocaleCallbacks.Count : -1;
                }
            }
            """, string.Empty, value => value > 0);
    }
}
