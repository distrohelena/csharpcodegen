using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Compiles and runs generated C++ that calls real Windows APIs, and compares each result with the same C# run as managed
/// P/Invoke. Every probe is compiled and run natively for both x86 and x64 (MSVC <c>-arch=x86</c> and <c>-arch=amd64</c>),
/// because stdcall decoration and small-struct return conventions only differ on x86. The managed side runs in the test
/// process architecture, so every probe returns a value that does not depend on the architecture. The tests return early
/// on non-Windows hosts (the xunit 2 runner used here has no dynamic skip), following the repository's existing pattern.
/// </summary>
[Trait("Category", "NativeCompile")]
public sealed class CPPPInvokeNativeExecutionTests {
    /// <summary>
    /// Converts the probe, compiles and runs it natively for one architecture, and asserts the printed value passes the
    /// sanity check and equals the value the same source returns when run as managed .NET. The program also prints its
    /// pointer size, which proves the requested architecture was actually built.
    /// </summary>
    /// <param name="source">C# source declaring a static <c>Probe.Run()</c> that returns a <c>long</c>.</param>
    /// <param name="prelude">C++ text written before the generated unity include; empty for none.</param>
    /// <param name="architecture">VsDevCmd target architecture (<c>x86</c> or <c>amd64</c>).</param>
    /// <param name="sanity">Predicate the native value must satisfy on its own.</param>
    static void AssertNativeMatchesManaged(string source, string prelude, string architecture, Func<long, bool> sanity) {
        var output = CPPCompileValidationRegressionTests.RunConversion(source, allowUnsafe: true);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run(
            "std::cout << static_cast<long long>(Probe::Run()) << std::endl << sizeof(void*) << std::endl;", prelude, architecture);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.Equal(0, result.ProgramExitCode);
        string[] outputLines = result.ProgramOutput.Trim().Split(Environment.NewLine);
        long native = long.Parse(outputLines[0]);
        Assert.Equal(architecture == "x86" ? "4" : "8", outputLines[1].Trim());
        long managed = CPPManagedSnippetRunner.Run(source, "Probe", "Run");
        Assert.True(sanity(native), $"native value {native} failed the sanity check");
        Assert.Equal(managed, native);
    }

    /// <summary>
    /// Ensures the simplest import compiles, links against kernel32 and returns the same success flag in both modes.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void GetTickCount64_CompilesLinksAndMatchesManaged(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public static class Probe {
                [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
                public static long Run() { return GetTickCount64() > 0 ? 1 : 0; }
            }
            """, string.Empty, architecture, value => value == 1);
    }

    /// <summary>
    /// Ensures an address obtained from native code (GetProcAddress) casts to a stdcall unmanaged function pointer and is
    /// invoked through it with the declared convention. The module and symbol names are passed as pointers into
    /// stackalloc buffers because the codegen does not support <c>fixed</c>.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void GetProcAddress_CastToUnmanagedFunctionPointer_InvokesAndMatchesManaged(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public static unsafe class Probe {
                [DllImport("kernel32.dll")] static extern nint GetModuleHandleW(ushort* moduleName);
                [DllImport("kernel32.dll")] static extern nint GetProcAddress(nint module, byte* procName);
                public static long Run() {
                    ushort* moduleName = stackalloc ushort[] { 'k', 'e', 'r', 'n', 'e', 'l', '3', '2', '.', 'd', 'l', 'l', 0 };
                    byte* procName = stackalloc byte[] { (byte)'G', (byte)'e', (byte)'t', (byte)'T', (byte)'i', (byte)'c', (byte)'k', (byte)'C', (byte)'o', (byte)'u', (byte)'n', (byte)'t', (byte)'6', (byte)'4', 0 };
                    nint address = GetProcAddress(GetModuleHandleW(moduleName), procName);
                    if (address == 0) {
                        return -1;
                    }
                    delegate* unmanaged[Stdcall]<ulong> getTickCount64 = (delegate* unmanaged[Stdcall]<ulong>)address;
                    return getTickCount64() > 0 ? 1 : 0;
                }
            }
            """, string.Empty, architecture, value => value == 1);
    }

    /// <summary>
    /// Ensures a by-ref struct and an 8-byte by-value struct carry real data: PtInRect must report the inside point as
    /// inside (bit 0) and the outside point as outside (bit 1 clear), in a unity build that already includes Windows.h,
    /// proving the forwarder isolation and the layout of both structs.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void PtInRect_ByRefRectAndByValuePoint_WithWindowsHeaderInUnity_MatchesManaged(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public struct NativePoint { public int X; public int Y; }
            public struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
            public static class Probe {
                [DllImport("user32.dll")] static extern int PtInRect(ref NativeRect rect, NativePoint point);
                public static long Run() {
                    NativeRect rect = new NativeRect { Left = 10, Top = 20, Right = 110, Bottom = 220 };
                    NativePoint inside = new NativePoint { X = 50, Y = 200 };
                    NativePoint outside = new NativePoint { X = 50, Y = 10 };
                    long insideBit = PtInRect(ref rect, inside) != 0 ? 1 : 0;
                    long outsideBit = PtInRect(ref rect, outside) != 0 ? 2 : 0;
                    return insideBit | outsideBit;
                }
            }
            """, "#include <Windows.h>", architecture, value => value == 1);
    }

    /// <summary>
    /// Ensures an out struct with pointer-sized fields is filled by native code at the right offsets: GetNativeSystemInfo
    /// must report the same page size natively and managed.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void GetNativeSystemInfo_OutStructWithPointerFields_MatchesManagedPageSize(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public struct SystemInfo {
                public ushort ProcessorArchitecture;
                public ushort Reserved;
                public uint PageSize;
                public nint MinimumApplicationAddress;
                public nint MaximumApplicationAddress;
                public nuint ActiveProcessorMask;
                public uint NumberOfProcessors;
                public uint ProcessorType;
                public uint AllocationGranularity;
                public ushort ProcessorLevel;
                public ushort ProcessorRevision;
            }
            public static class Probe {
                [DllImport("kernel32.dll")] static extern void GetNativeSystemInfo(out SystemInfo info);
                public static long Run() {
                    GetNativeSystemInfo(out SystemInfo info);
                    return info.PageSize;
                }
            }
            """, string.Empty, architecture, value => value > 0);
    }

    /// <summary>
    /// Ensures a pointer returned through a forwarder converts back to the generated pointer type and can be read:
    /// the first character of GetCommandLineW is never zero.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void GetCommandLineW_PointerReturn_MatchesManaged(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public static unsafe class Probe {
                [DllImport("kernel32.dll")] static extern ushort* GetCommandLineW();
                public static long Run() {
                    ushort* commandLine = GetCommandLineW();
                    return commandLine[0] != 0 ? 1 : 0;
                }
            }
            """, string.Empty, architecture, value => value == 1);
    }

    /// <summary>
    /// Ensures a small struct returned by value through a forwarder uses the platform return convention (in a register on
    /// x86): GetLargestConsoleWindowSize returns a zero COORD for an invalid handle in both modes.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void GetLargestConsoleWindowSize_StructReturn_MatchesManaged(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            public struct Coord { public short X; public short Y; }
            public static class Probe {
                [DllImport("kernel32.dll")] static extern Coord GetLargestConsoleWindowSize(nint consoleOutput);
                public static long Run() {
                    Coord size = GetLargestConsoleWindowSize(-1);
                    return size.X == 0 && size.Y == 0 ? 1 : 0;
                }
            }
            """, string.Empty, architecture, value => value == 1);
    }

    /// <summary>
    /// Ensures an explicit-layout struct filled through an out parameter keeps its overlapping fields at their
    /// FieldOffsets: QueryPerformanceFrequency's LowPart must equal the low 32 bits of QuadPart.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void QueryPerformanceFrequency_ExplicitLayoutOutStruct_MatchesManaged(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        AssertNativeMatchesManaged("""
            using System.Runtime.InteropServices;
            [StructLayout(LayoutKind.Explicit)] public struct LargeInteger { [FieldOffset(0)] public long QuadPart; [FieldOffset(0)] public uint LowPart; [FieldOffset(4)] public int HighPart; }
            public static class Probe {
                [DllImport("kernel32.dll")] static extern int QueryPerformanceFrequency(out LargeInteger frequency);
                public static long Run() {
                    QueryPerformanceFrequency(out LargeInteger frequency);
                    return frequency.QuadPart > 0 && frequency.LowPart == (uint)frequency.QuadPart && frequency.HighPart == (int)(frequency.QuadPart >> 32) ? 1 : 0;
                }
            }
            """, string.Empty, architecture, value => value == 1);
    }

    /// <summary>
    /// Ensures a stdcall callback round trip enumerates the same number of locales natively and managed, with lParam intact.
    /// The callback's parameters are named with C++ keywords to prove the trampoline and the method agree on the
    /// sanitized names.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void EnumSystemLocalesEx_CallbackRoundTrip_MatchesManaged(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        AssertNativeMatchesManaged("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            public static unsafe class Probe {
                static long Count;
                static long BadParameter;
                [DllImport("kernel32.dll")] static extern int EnumSystemLocalesEx(delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback, uint flags, nint lParam, nint reserved);
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                static int OnLocale(ushort* name, uint @struct, nint template) {
                    if (template != 42) { BadParameter++; }
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
            """, string.Empty, architecture, value => value > 0);
    }

    /// <summary>
    /// Ensures a callback declared in one class can have its address taken in another class and passed to a native
    /// enumerator, so the calling class's translation unit sees the declaring class's trampoline, and that the
    /// native enumeration count matches the managed one.
    /// </summary>
    /// <param name="architecture">VsDevCmd target architecture.</param>
    [Theory]
    [InlineData("x86")]
    [InlineData("amd64")]
    public void EnumSystemLocalesEx_CallbackDeclaredInAnotherClass_MatchesManaged(string architecture) {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

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
            """, string.Empty, architecture, value => value > 0);
    }
}
