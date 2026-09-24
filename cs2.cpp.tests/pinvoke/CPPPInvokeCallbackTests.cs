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
    /// Source that passes null constants for an unmanaged function-pointer parameter and a by-value struct whose fields
    /// are private (the C# default) to DllImports.
    /// </summary>
    const string NullCallbackAndPrivateFieldsSource = """
        using System.Runtime.InteropServices;
        public struct Hidden { int A; int B; public Hidden(int a, int b) { A = a; B = b; } }
        public static unsafe class Probe {
            [DllImport("kernel32.dll")] static extern int EnumSystemLocalesEx(delegate* unmanaged[Stdcall]<ushort*, uint, nint, int> callback, uint flags, nint lParam, nint reserved);
            [DllImport("user32.dll")] static extern nint WindowFromPoint(Hidden point);
            public static int Run() {
                int first = EnumSystemLocalesEx(null, 0, 0, 0);
                int second = EnumSystemLocalesEx(default, 0, 0, 0);
                WindowFromPoint(new Hidden(1, 2));
                return first + second;
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
        Assert.Contains("friend int32_t HE_CPP_STDCALL he_pinvoke_cb_Locales_OnLocale(", header, StringComparison.Ordinal);
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

    /// <summary>
    /// Ensures null and default arguments for an unmanaged function-pointer parameter become a typed nullptr instead of
    /// being unwrapped through he_cpp_raw_function_pointer, which has no overload for nullptr.
    /// </summary>
    [Fact]
    public void WriteOutput_NullFunctionPointerArgument_PassesTypedNullptr() {
        var output = CPPCompileValidationRegressionTests.RunConversion(NullCallbackAndPrivateFieldsSource, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Probe.cpp"));
        Assert.Contains("he_pinvoke::kernel32::EnumSystemLocalesEx(static_cast<int32_t (HE_CPP_STDCALL*)(void*,uint32_t,intptr_t)>(nullptr), ", source, StringComparison.Ordinal);
        Assert.Equal(2, source.Split("static_cast<int32_t (HE_CPP_STDCALL*)(void*,uint32_t,intptr_t)>(nullptr)").Length - 1);
        Assert.DoesNotContain("he_cpp_raw_function_pointer(nullptr)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a mirrored struct with private fields befriends its layout-check struct, and the checker holding the
    /// layout assertions is defined in the struct's source file.
    /// </summary>
    [Fact]
    public void WriteOutput_MirrorStructWithPrivateFields_EmitsFriendLayoutCheck() {
        var output = CPPCompileValidationRegressionTests.RunConversion(NullCallbackAndPrivateFieldsSource, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "Hidden.hpp"));
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Hidden.cpp"));
        Assert.Contains("    friend struct he_pinvoke_layout_check_Hidden;", header, StringComparison.Ordinal);
        Assert.Contains("struct he_pinvoke_layout_check_Hidden {", source, StringComparison.Ordinal);
        Assert.Contains("    static_assert(sizeof(Hidden) == sizeof(he_pinvoke_Hidden)", source, StringComparison.Ordinal);
        Assert.Contains("    static_assert(offsetof(Hidden, A) == offsetof(he_pinvoke_Hidden, A)", source, StringComparison.Ordinal);
        Assert.Contains("    static_assert(offsetof(Hidden, B) == offsetof(he_pinvoke_Hidden, B)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a callback whose parameters are C++ keywords (a verbatim C# identifier and a C++-only keyword) declares
    /// sanitized parameter names and forwards exactly those names, matching the references in the method body.
    /// </summary>
    [Fact]
    public void WriteOutput_CallbackWithKeywordParameters_ForwardsSanitizedNames() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            public static class Hooks {
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                static int OnKey(int @class, nint template) { return @class + (int)template; }
            }
            """, allowUnsafe: true);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "Hooks.hpp"));
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Hooks.cpp"));
        Assert.Contains("he_pinvoke_cb_Hooks_OnKey(int32_t class_, intptr_t template_) noexcept;", header, StringComparison.Ordinal);
        Assert.Contains("static int32_t OnKey(int32_t class_, intptr_t template_);", header, StringComparison.Ordinal);
        Assert.Contains("return Hooks::OnKey(class_, template_);", source, StringComparison.Ordinal);
        Assert.Contains("return class_ + static_cast<int32_t>(template_);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@class", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures casts between native-sized integers or void* and unmanaged function pointers go through the wrapper's
    /// raw pointer type in both directions, instead of a static_cast the wrapper does not support.
    /// </summary>
    [Fact]
    public void WriteOutput_UnmanagedFunctionPointerCasts_ReinterpretRawPointer() {
        var output = CPPCompileValidationRegressionTests.RunConversion("""
            public static unsafe class Casts {
                public static nint RoundTrip(nint value) {
                    delegate* unmanaged[Stdcall]<int, int> pointer = (delegate* unmanaged[Stdcall]<int, int>)value;
                    return (nint)pointer;
                }
                public static nuint ToUnsigned(delegate* unmanaged[Cdecl]<int> pointer) { return (nuint)pointer; }
                public static void* ToVoid(delegate* unmanaged[Cdecl]<int> pointer) { return (void*)pointer; }
                public static int FromVoid(void* address) { delegate* unmanaged[Cdecl]<int> pointer = (delegate* unmanaged[Cdecl]<int>)address; return pointer(); }
            }
            """, allowUnsafe: true);
        string source = File.ReadAllText(Path.Combine(output.OutputPath, "Casts.cpp"));
        Assert.Contains("StdcallFunctionPointer<int32_t, int32_t>(reinterpret_cast<StdcallFunctionPointer<int32_t, int32_t>::PointerType>(value))", source, StringComparison.Ordinal);
        Assert.Contains("reinterpret_cast<intptr_t>(he_cpp_raw_function_pointer(pointer))", source, StringComparison.Ordinal);
        Assert.Contains("reinterpret_cast<uintptr_t>(he_cpp_raw_function_pointer(pointer))", source, StringComparison.Ordinal);
        Assert.Contains("reinterpret_cast<void*>(he_cpp_raw_function_pointer(pointer))", source, StringComparison.Ordinal);
        Assert.Contains("CdeclFunctionPointer<int32_t>(reinterpret_cast<CdeclFunctionPointer<int32_t>::PointerType>(address))", source, StringComparison.Ordinal);
    }
}
