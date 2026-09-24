namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// End-to-end conversion tests for DllImport and UnmanagedCallersOnly lowering through the real converter pipeline.
/// </summary>
public sealed class CPPPInvokeConversionTests {
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
}
