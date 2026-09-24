namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Verifies UnmanagedCallersOnly trampolines, unmanaged function-pointer lowering, and mirror layout assertions.
/// </summary>
public sealed class CPPPInvokeCallbackTests {
    /// <summary>
    /// Source with a stdcall enumeration callback passed to a DllImport.
    /// </summary>
    const string Source = """
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        public struct NativePoint { public int X; public int Y; }
        public static unsafe class Locales {
            static int Count;
            [DllImport("kernel32.dll")] static extern int EnumSystemLocalesEx(delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback, uint flags, nint lParam, nint reserved);
            [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
            static int OnLocale(ushort* name, uint flags, nint lParam) { Count++; return 1; }
            public static int Run() {
                Count = 0;
                delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback = &OnLocale;
                EnumSystemLocalesEx(callback, 0, 42, 0);
                return Count;
            }
        }
        """;

    /// <summary>
    /// Ensures the trampoline is emitted with the calling convention, noexcept and a forwarding body.
    /// </summary>
    [Fact]
    public void WriteOutput_Callback_EmitsNoexceptTrampoline() {
        var output = CPPCompileValidationRegressionTests.RunConversion(Source, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "Locales.hpp"));
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Locales.cpp"));
        Assert.Contains("int32_t HE_CPP_STDCALL he_pinvoke_cb_Locales_OnLocale(", header, StringComparison.Ordinal);
        Assert.Contains(") noexcept;", header, StringComparison.Ordinal);
        Assert.Contains("return Locales::OnLocale(name, flags, lParam);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the unmanaged function-pointer local uses the stdcall wrapper, takes the trampoline address, and is unwrapped at the call.
    /// </summary>
    [Fact]
    public void WriteOutput_UnmanagedFunctionPointer_UsesTrampolineAndRawPointer() {
        var output = CPPCompileValidationRegressionTests.RunConversion(Source, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Locales.cpp"));
        Assert.Contains("StdcallFunctionPointer<int32_t, uint16_t*, uint32_t, intptr_t> callback = &he_pinvoke_cb_Locales_OnLocale;", source, StringComparison.Ordinal);
        Assert.Contains("reinterpret_cast<int32_t (HE_CPP_STDCALL*)(void*,uint32_t,intptr_t)>(he_cpp_raw_function_pointer(callback))", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a by-value mirrored struct gets size, alignment and per-field offset assertions in its own source file.
    /// </summary>
    [Fact]
    public void WriteOutput_MirrorStruct_EmitsLayoutAsserts() {
        var output = CPPCompileValidationRegressionTests.RunConversion(Source, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "NativePoint.cpp"));
        Assert.Contains("static_assert(sizeof(NativePoint) == sizeof(he_pinvoke_NativePoint)", source, StringComparison.Ordinal);
        Assert.Contains("static_assert(alignof(NativePoint) == alignof(he_pinvoke_NativePoint)", source, StringComparison.Ordinal);
        Assert.Contains("static_assert(offsetof(NativePoint, Y) == offsetof(he_pinvoke_NativePoint, Y)", source, StringComparison.Ordinal);
    }
}
