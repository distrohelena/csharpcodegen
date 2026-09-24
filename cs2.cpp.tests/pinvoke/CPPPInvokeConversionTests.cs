namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// End-to-end conversion tests for DllImport and UnmanagedCallersOnly lowering through the real converter pipeline.
/// </summary>
public sealed class CPPPInvokeConversionTests {
    /// <summary>
    /// Shared source importing two user32 symbols, one kernel32 symbol, and a by-value struct.
    /// </summary>
    const string ImportsSource = """
        using System.Runtime.InteropServices;
        namespace Native {
            public struct NativePoint { public int X; public int Y; }
            public static class User32 {
                [DllImport("user32.dll")] public static extern int SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
                [DllImport("user32.dll")] public static extern int GetCursorPos(out NativePoint lpPoint);
                [DllImport("user32.dll")] public static extern nint WindowFromPoint(NativePoint Point);
            }
            public static class Kernel32 { [DllImport("kernel32.dll")] public static extern ulong GetTickCount64(); }
            public static class Other { [DllImport("user32.dll")] public static extern nint WindowFromPoint(NativePoint Point); }
        }
        """;

    /// <summary>
    /// Source importing exactly the three user32 symbols whose generated header and source text the spec pins verbatim.
    /// </summary>
    const string User32OnlySource = """
        using System.Runtime.InteropServices;
        namespace Native {
            public struct NativePoint { public int X; public int Y; }
            public static class User32 {
                [DllImport("user32.dll")] public static extern int SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
                [DllImport("user32.dll")] public static extern int GetCursorPos(out NativePoint lpPoint);
                [DllImport("user32.dll")] public static extern nint WindowFromPoint(NativePoint Point);
            }
        }
        """;

    /// <summary>
    /// Expected generated header for <see cref="User32OnlySource"/>, pinned verbatim by the design spec.
    /// </summary>
    const string ExpectedUser32Header = """
        // Generated direct P/Invoke forwarders. Do not edit.
        #ifndef HE_PINVOKE_NATIVE_IMPORTS_HPP
        #define HE_PINVOKE_NATIVE_IMPORTS_HPP

        #include <cstdint>
        #include <cstring>

        #include "../runtime/native_calling_convention.hpp"

        struct he_pinvoke_Native_NativePoint {
            int32_t X;
            int32_t Y;
        };

        template <typename TTo, typename TFrom>
        inline TTo he_pinvoke_bit_copy(const TFrom& value) {
            static_assert(sizeof(TTo) == sizeof(TFrom), "he_pinvoke_bit_copy requires layout-compatible types.");
            TTo result;
            std::memcpy(&result, &value, sizeof(TTo));
            return result;
        }

        namespace he_pinvoke::user32 {
            int32_t GetCursorPos(void* lpPoint);
            int32_t SetWindowPos(intptr_t hWnd, intptr_t hWndInsertAfter, int32_t X, int32_t Y, int32_t cx, int32_t cy, uint32_t uFlags);
            intptr_t WindowFromPoint(he_pinvoke_Native_NativePoint Point);
        }

        #endif

        """;

    /// <summary>
    /// Expected generated forwarder source for <see cref="User32OnlySource"/>, pinned verbatim by the design spec.
    /// </summary>
    const string ExpectedUser32Source = """
        // Generated direct P/Invoke forwarders. Do not edit.
        #include "native_imports.hpp"

        extern "C" int32_t HE_CPP_STDCALL GetCursorPos(void* lpPoint);
        extern "C" int32_t HE_CPP_STDCALL SetWindowPos(intptr_t hWnd, intptr_t hWndInsertAfter, int32_t X, int32_t Y, int32_t cx, int32_t cy, uint32_t uFlags);
        extern "C" intptr_t HE_CPP_STDCALL WindowFromPoint(he_pinvoke_Native_NativePoint Point);

        namespace he_pinvoke::user32 {
            int32_t GetCursorPos(void* lpPoint) {
                return ::GetCursorPos(lpPoint);
            }

            int32_t SetWindowPos(intptr_t hWnd, intptr_t hWndInsertAfter, int32_t X, int32_t Y, int32_t cx, int32_t cy, uint32_t uFlags) {
                return ::SetWindowPos(hWnd, hWndInsertAfter, X, Y, cx, cy, uFlags);
            }

            intptr_t WindowFromPoint(he_pinvoke_Native_NativePoint Point) {
                return ::WindowFromPoint(Point);
            }
        }

        """;

    /// <summary>
    /// Ensures an invalid DllImport fails the conversion with a source-located CPPPINV error.
    /// </summary>
    [Fact]
    public void AddCsproj_InvalidImport_FailsWithCPPPINV() {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static class U {
                [DllImport("user32.dll")] public static extern int MessageBeep(bool flag);
                public static int Run() { return MessageBeep(true); }
            }
            """, allowUnsafe: true));
        Assert.StartsWith("CPPPINV002", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures an extern import method has no generated member declaration or runtime-throwing stub.
    /// </summary>
    [Fact]
    public void WriteOutput_ExternImport_SkipsStubEmission() {
        CPPCompileValidationRegressionTests.ConversionOutput output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static class Clock {
                [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
                public static ulong Now() { return GetTickCount64(); }
            }
            """, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "Clock.hpp"));
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Clock.cpp"));
        Assert.DoesNotContain("GetTickCount64(", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Method has no generated body", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the header holds only portable includes, mirrors and namespaced forwarder declarations.
    /// </summary>
    [Fact]
    public void WriteOutput_Imports_EmitsIsolatedHeader() {
        var output = CPPCompileValidationRegressionTests.RunConversion(ImportsSource, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "native_imports", "native_imports.hpp"));
        Assert.DoesNotContain("Windows.h", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("struct he_pinvoke_Native_NativePoint {", header, StringComparison.Ordinal);
        Assert.Contains("namespace he_pinvoke::user32 {", header, StringComparison.Ordinal);
        Assert.Contains("int32_t GetCursorPos(void* lpPoint);", header, StringComparison.Ordinal);
        Assert.Contains("namespace he_pinvoke::kernel32 {", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures one extern "C" prototype and forwarder is emitted per distinct symbol even when imported twice.
    /// </summary>
    [Fact]
    public void WriteOutput_DuplicateImports_EmitsSingleForwarder() {
        var output = CPPCompileValidationRegressionTests.RunConversion(ImportsSource, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "native_imports", "native_imports.cpp"));
        Assert.Equal(1, CountOccurrences(source, "extern \"C\" intptr_t HE_CPP_STDCALL WindowFromPoint("));
        Assert.Contains("return ::GetTickCount64();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the unity build excludes the isolated translation unit and the handoff publishes it with link libraries.
    /// </summary>
    [Fact]
    public void WriteOutput_Imports_ExcludedFromUnityAndPublishedInHandoff() {
        var output = CPPCompileValidationRegressionTests.RunConversion(ImportsSource, allowUnsafe: true);
        string unity = File.ReadAllText(Path.Combine(output.OutputPath, "generated_unity.cpp"));
        string handoff = File.ReadAllText(Path.Combine(output.OutputPath, "generated_windows_handoff.cmake"));
        string msvc = File.ReadAllText(Path.Combine(output.OutputPath, "build_msvc.bat"));
        Assert.DoesNotContain("native_imports/native_imports.cpp", unity, StringComparison.Ordinal);
        Assert.Contains("set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"${CPP_GENERATED_CORE_ROOT}/native_imports/native_imports.cpp\")", handoff, StringComparison.Ordinal);
        Assert.Contains("set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"kernel32;user32\")", handoff, StringComparison.Ordinal);
        Assert.Contains("native_imports.obj", msvc, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures projects without imports keep their previous output shape.
    /// </summary>
    [Fact]
    public void WriteOutput_NoImports_DoesNotEmitNativeImportsFolder() {
        var output = CPPCompileValidationRegressionTests.RunConversion("public static class Plain { public static int Two() { return 2; } }");
        Assert.False(Directory.Exists(Path.Combine(output.OutputPath, "native_imports")));
        string handoff = File.ReadAllText(Path.Combine(output.OutputPath, "generated_windows_handoff.cmake"));
        Assert.Contains("set(CPP_GENERATED_NATIVE_IMPORTS_SOURCE \"\")", handoff, StringComparison.Ordinal);
        Assert.Contains("set(CPP_GENERATED_NATIVE_LINK_LIBRARIES \"\")", handoff, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the generated header and forwarder source match the pinned text exactly, and that the calling-convention
    /// runtime header they depend on is present in the output runtime folder.
    /// </summary>
    [Fact]
    public void WriteOutput_User32Imports_EmitsExactHeaderAndSource() {
        var output = CPPCompileValidationRegressionTests.RunConversion(User32OnlySource, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "native_imports", "native_imports.hpp")).ReplaceLineEndings("\n");
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "native_imports", "native_imports.cpp")).ReplaceLineEndings("\n");
        Assert.Equal(ExpectedUser32Header.ReplaceLineEndings("\n"), header);
        Assert.Equal(ExpectedUser32Source.ReplaceLineEndings("\n"), source);
        Assert.True(File.Exists(Path.Combine(output.OutputPath, "runtime", "native_calling_convention.hpp")));
    }

    /// <summary>
    /// Ensures packed mirrors are wrapped in pack pragmas, function-pointer parameters place the parameter name inside
    /// the declarator, and void forwarders call the native symbol without returning.
    /// </summary>
    [Fact]
    public void WriteOutput_PackedStructFunctionPointerAndVoidImport_EmitsValidDeclarations() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            namespace Native {
                [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct Packed { public byte A; public int B; }
                public static unsafe class Api {
                    [DllImport("api.dll")] public static extern void Consume(Packed value);
                    [DllImport("api.dll")] public static extern void Register(delegate* unmanaged[Stdcall]<int, int> callback);
                }
            }
            """, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "native_imports", "native_imports.hpp")).ReplaceLineEndings("\n");
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "native_imports", "native_imports.cpp")).ReplaceLineEndings("\n");
        Assert.Contains("#pragma pack(push, 1)\nstruct he_pinvoke_Native_Packed {\n    uint8_t A;\n    int32_t B;\n};\n#pragma pack(pop)\n", header, StringComparison.Ordinal);
        Assert.Contains("void Register(int32_t (HE_CPP_STDCALL* callback)(int32_t));", header, StringComparison.Ordinal);
        Assert.Contains("extern \"C\" void HE_CPP_STDCALL Consume(he_pinvoke_Native_Packed value);", source, StringComparison.Ordinal);
        Assert.Contains("        ::Consume(value);\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return ::Consume", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a primitive import call targets the namespaced forwarder and includes the forwarder header.
    /// </summary>
    [Fact]
    public void WriteOutput_PrimitiveImportCall_UsesForwarder() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static class Clock {
                [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
                public static ulong Now() { return GetTickCount64(); }
            }
            """, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Clock.cpp"));
        Assert.Contains("#include \"native_imports/native_imports.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("he_pinvoke::kernel32::GetTickCount64()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures out-var struct arguments declare the local first and pass its address as void*.
    /// </summary>
    [Fact]
    public void WriteOutput_OutVarStructArgument_DeclaresLocalAndPassesAddress() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public struct NativePoint { public int X; public int Y; }
            public static class Cursor {
                [DllImport("user32.dll")] static extern int GetCursorPos(out NativePoint point);
                public static int ReadX() { GetCursorPos(out NativePoint p); return p.X; }
            }
            """, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Cursor.cpp"));
        Assert.Contains("he_pinvoke::user32::GetCursorPos(static_cast<void*>(&(p)))", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("NativePoint p", StringComparison.Ordinal) < source.IndexOf("he_pinvoke::user32::GetCursorPos", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures enum arguments and returns cast through the underlying integer type.
    /// </summary>
    [Fact]
    public void WriteOutput_EnumArgumentAndReturn_CastsThroughUnderlyingType() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public enum Show : int { Hide = 0, Normal = 1 }
            public static class Win {
                [DllImport("user32.dll")] static extern Show ShowWindow(nint hWnd, Show command);
                public static Show Hide(nint hWnd) { return ShowWindow(hWnd, Show.Hide); }
            }
            """, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Win.cpp"));
        Assert.Contains("static_cast<int32_t>(", source, StringComparison.Ordinal);
        Assert.Contains("static_cast<::Show>(he_pinvoke::user32::ShowWindow(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures pointer arguments cross as void* and by-value structs are bit-copied into mirrors.
    /// </summary>
    [Fact]
    public void WriteOutput_PointerAndByValueStruct_AreConverted() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public struct NativePoint { public int X; public int Y; }
            public static unsafe class Win {
                [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
                [DllImport("user32.dll")] static extern int GetWindowRect(nint hWnd, int* rect);
                public static nint At(NativePoint point) { return WindowFromPoint(point); }
                public static int Rect(nint hWnd, int* rect) { return GetWindowRect(hWnd, rect); }
            }
            """, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Win.cpp"));
        Assert.Contains("he_pinvoke::user32::WindowFromPoint(he_pinvoke_bit_copy<he_pinvoke_NativePoint>(point))", source, StringComparison.Ordinal);
        Assert.Contains("he_pinvoke::user32::GetWindowRect(hWnd, static_cast<void*>(rect))", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a null literal passed to a pointer parameter converts through static_cast, which accepts nullptr.
    /// </summary>
    [Fact]
    public void WriteOutput_NullPointerArgument_UsesStaticCast() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static unsafe class Module {
                [DllImport("kernel32.dll")] static extern nint GetModuleHandleW(ushort* name);
                public static nint Self() { return GetModuleHandleW(null); }
            }
            """, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Module.cpp"));
        Assert.Contains("he_pinvoke::kernel32::GetModuleHandleW(static_cast<void*>(nullptr))", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures an out-variable declared in a multi-argument call is hoisted before the statement and passed by address.
    /// </summary>
    [Fact]
    public void WriteOutput_MultiArgumentOutVar_HoistsDeclarationAndPassesAddress() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static class Threads {
                [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
                public static uint Sum(nint hWnd) { uint thread = GetWindowThreadProcessId(hWnd, out uint pid); return pid + thread; }
            }
            """, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Threads.cpp"));
        int callIndex = source.IndexOf("he_pinvoke::user32::GetWindowThreadProcessId(hWnd, static_cast<void*>(&(pid)))", StringComparison.Ordinal);
        int declarationIndex = source.IndexOf("uint32_t pid;", StringComparison.Ordinal);
        Assert.True(callIndex >= 0, source);
        Assert.True(declarationIndex >= 0 && declarationIndex < callIndex, source);
    }

    /// <summary>
    /// Ensures two out-variables declared in one call are both hoisted before the statement and passed by address.
    /// </summary>
    [Fact]
    public void WriteOutput_TwoOutVars_HoistsBothDeclarations() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static class Pair {
                [DllImport("api.dll")] static extern int GetPair(out int a, out int b);
                public static int Sum() { GetPair(out int a, out var b); return a + b; }
            }
            """, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Pair.cpp"));
        int callIndex = source.IndexOf("he_pinvoke::api::GetPair(static_cast<void*>(&(a)), static_cast<void*>(&(b)))", StringComparison.Ordinal);
        int firstDeclarationIndex = source.IndexOf("int32_t a;", StringComparison.Ordinal);
        int secondDeclarationIndex = source.IndexOf("int32_t b;", StringComparison.Ordinal);
        Assert.True(callIndex >= 0, source);
        Assert.True(firstDeclarationIndex >= 0 && firstDeclarationIndex < callIndex, source);
        Assert.True(secondDeclarationIndex >= 0 && secondDeclarationIndex < callIndex, source);
    }

    /// <summary>
    /// Ensures a DllImport call written with named arguments is rejected instead of being silently reordered.
    /// </summary>
    [Fact]
    public void WriteOutput_NamedImportArgument_Throws() {
        Exception exception = Record.Exception(() => CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.InteropServices;
            public static class Win {
                [DllImport("user32.dll")] static extern int MoveTo(int x, int y);
                public static int Move() { return MoveTo(y: 2, x: 1); }
            }
            """, allowUnsafe: true));
        Assert.NotNull(exception);
        Assert.Contains("named or omitted arguments are not supported for DllImport calls", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Counts ordinal occurrences of a fragment in generated text.
    /// </summary>
    /// <param name="text">Generated text to search.</param>
    /// <param name="fragment">Fragment whose non-overlapping occurrences are counted.</param>
    /// <returns>The number of non-overlapping ordinal occurrences of <paramref name="fragment"/>.</returns>
    static int CountOccurrences(string text, string fragment) {
        int count = 0;
        int index = text.IndexOf(fragment, StringComparison.Ordinal);
        while (index >= 0) {
            count++;
            index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
        }
        return count;
    }
}
