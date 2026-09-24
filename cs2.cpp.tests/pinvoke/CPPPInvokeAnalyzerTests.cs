using cs2.cpp;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Verifies import-table construction, de-duplication, and every CPPPINV rejection produced by the analyzer.
/// </summary>
public sealed class CPPPInvokeAnalyzerTests {
    /// <summary>
    /// Analyzes one source snippet.
    /// </summary>
    static CPPPInvokeAnalysisResult Analyze(string source) {
        return new CPPPInvokeAnalyzer().Analyze(new[] { (Compilation)CPPPInvokeTestCompilation.Create(source) });
    }

    /// <summary>
    /// Ensures a valid import produces one entry with an exact symbol, library namespace and stdcall convention.
    /// </summary>
    [Fact]
    public void Analyze_ValidImport_BuildsImportTable() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.InteropServices;
            static class U {
                [DllImport("User32.dll", EntryPoint = "SetWindowPos", ExactSpelling = true)]
                static extern int Move(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
            }
            """);

        Assert.False(result.HasErrors);
        CPPPInvokeImport import = Assert.Single(result.Plan.Imports);
        Assert.Equal("user32", import.LibraryNamespace);
        Assert.Equal("SetWindowPos", import.EntryPoint);
        Assert.Equal("HE_CPP_STDCALL int32_t(intptr_t,intptr_t,int32_t,int32_t,int32_t,int32_t,uint32_t)", import.Signature.MirrorKey);
        Assert.Equal(new[] { "user32" }, result.Plan.LinkLibraries);
    }

    /// <summary>
    /// Ensures the same symbol imported by two classes with an identical signature yields one import holding both method ids.
    /// </summary>
    [Fact]
    public void Analyze_DuplicateImportsWithSameSignature_EmitsOneImport() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.InteropServices;
            static class A { [DllImport("user32.dll")] public static extern nint GetForegroundWindow(); }
            static class B { [DllImport("user32")] public static extern nint GetForegroundWindow(); }
            """);

        Assert.False(result.HasErrors);
        CPPPInvokeImport import = Assert.Single(result.Plan.Imports);
        Assert.Equal(2, import.MethodIds.Count);
    }

    /// <summary>
    /// Ensures each invalid declaration produces its stable diagnostic code.
    /// </summary>
    [Theory]
    [InlineData("[DllImport(\"user32.dll\")] static extern int F(bool value);", "CPPPINV002")]
    [InlineData("[DllImport(\"user32.dll\", SetLastError = true)] static extern int F();", "CPPPINV003")]
    [InlineData("[DllImport(\"user32.dll\", CharSet = CharSet.Unicode)] static extern int F();", "CPPPINV003")]
    [InlineData("[DllImport(\"user32.dll\", PreserveSig = false)] static extern int F();", "CPPPINV003")]
    [InlineData("[DllImport(\"user32.dll\")] static extern int F([MarshalAs(UnmanagedType.I4)] int value);", "CPPPINV003")]
    [InlineData("[DllImport(\"user32.dll\", CallingConvention = CallingConvention.ThisCall)] static extern int F();", "CPPPINV004")]
    [InlineData("[LibraryImport(\"user32.dll\")] private static partial int F(); private static partial int F() { return 0; }", "CPPPINV001")]
    public void Analyze_InvalidImport_ReportsCode(string declaration, string expectedCode) {
        CPPPInvokeAnalysisResult result = Analyze($$"""
            using System.Runtime.InteropServices;
            static partial class U {
                {{declaration}}
            }
            """);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode && diagnostic.Severity == CPPDiagnosticSeverity.Error && diagnostic.LineNumber > 0);
    }

    /// <summary>
    /// Ensures the same symbol with two different lowered signatures is rejected.
    /// </summary>
    [Fact]
    public void Analyze_ConflictingSignatures_ReportsCPPPINV005() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.InteropServices;
            static class A { [DllImport("user32.dll")] public static extern int GetKeyState(int key); }
            static class B { [DllImport("user32.dll")] public static extern short GetKeyState(int key); }
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPPINV005");
    }

    /// <summary>
    /// Ensures one symbol imported from two libraries is rejected.
    /// </summary>
    [Fact]
    public void Analyze_SymbolFromTwoLibraries_ReportsCPPPINV006() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.InteropServices;
            static class A { [DllImport("a.dll")] public static extern int Init(); }
            static class B { [DllImport("b.dll")] public static extern int Init(); }
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CPPPINV006");
    }

    /// <summary>
    /// Ensures valid callbacks get deterministic trampoline names and conventions.
    /// </summary>
    [Fact]
    public void Analyze_ValidCallbacks_BuildTrampolines() {
        CPPPInvokeAnalysisResult result = Analyze("""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            namespace Shell {
                static unsafe class Win {
                    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
                    static nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam) { return 0; }
                    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
                    static int Compare(void* a, void* b) { return 0; }
                }
            }
            """);

        Assert.False(result.HasErrors);
        Assert.Equal(new[] { "he_pinvoke_cb_Shell_Win_Compare", "he_pinvoke_cb_Shell_Win_WindowProc" }, result.Plan.Callbacks.Select(callback => callback.TrampolineName));
        Assert.Equal(CPPPInvokeCallingConvention.Cdecl, result.Plan.Callbacks[0].Signature.CallingConvention);
    }

    /// <summary>
    /// Ensures invalid callbacks are rejected with CPPPINV007 or CPPPINV008.
    /// </summary>
    [Theory]
    [InlineData("[UnmanagedCallersOnly(EntryPoint = \"Exported\")] static int F() { return 0; }", "CPPPINV007")]
    [InlineData("[UnmanagedCallersOnly] static int F() { return 0; } [UnmanagedCallersOnly] static int F(int a) { return a; }", "CPPPINV007")]
    [InlineData("[UnmanagedCallersOnly] static int F(P point) { return 0; }", "CPPPINV008")]
    [InlineData("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvThiscall) })] static int F() { return 0; }", "CPPPINV004")]
    public void Analyze_InvalidCallback_ReportsCode(string declaration, string expectedCode) {
        CPPPInvokeAnalysisResult result = Analyze($$"""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            struct P { public int X; }
            static class U {
                {{declaration}}
            }
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }
}
