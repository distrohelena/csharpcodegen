using cs2.cpp;
using Microsoft.CodeAnalysis;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Verifies the P/Invoke plan model: library-name normalization, signature keys, and plan lookups.
/// </summary>
public sealed class CPPPInvokeModelTests {
    /// <summary>
    /// Ensures library names lose directories and extensions and become lowercase C++ identifiers.
    /// </summary>
    [Theory]
    [InlineData("user32.dll", "user32")]
    [InlineData("USER32", "user32")]
    [InlineData("C:\\libs\\gevo-native.dll", "gevo_native")]
    [InlineData("libfoo.so", "libfoo")]
    [InlineData("3dlib.dylib", "_3dlib")]
    [InlineData("gevo_native.lib", "gevo_native")]
    public void Normalize_StripsDirectoryAndExtension(string moduleName, string expected) {
        Assert.Equal(expected, CPPPInvokeLibraryNameNormalizer.Normalize(moduleName));
    }

    /// <summary>
    /// Ensures a blank module name is rejected instead of producing an empty namespace.
    /// </summary>
    [Fact]
    public void Normalize_Blank_Throws() {
        Assert.Throws<ArgumentException>(() => CPPPInvokeLibraryNameNormalizer.Normalize("  "));
    }

    /// <summary>
    /// Ensures signature keys include the calling convention, return type and ref-lowered parameters.
    /// </summary>
    [Fact]
    public void MirrorKey_IncludesConventionReturnAndParameters() {
        CPPPInvokeLoweredType int32 = new CPPPInvokeLoweredType(CPPPInvokeValueKind.Primitive, "int32_t", null);
        CPPPInvokeLoweredType intPtr = new CPPPInvokeLoweredType(CPPPInvokeValueKind.Primitive, "intptr_t", null);
        CPPPInvokeSignature signature = new CPPPInvokeSignature(
            int32,
            new[] {
                new CPPPInvokeParameter("hWnd", intPtr, RefKind.None),
                new CPPPInvokeParameter("point", int32, RefKind.Out)
            },
            CPPPInvokeCallingConvention.StdCall);

        Assert.Equal("HE_CPP_STDCALL int32_t(intptr_t,void*)", signature.MirrorKey);
    }

    /// <summary>
    /// Ensures the plan exposes sorted distinct link libraries and resolves imports by method id.
    /// </summary>
    [Fact]
    public void Plan_LinkLibrariesAreSortedDistinct_AndImportsResolveByMethodId() {
        CPPPInvokeSignature signature = new CPPPInvokeSignature(
            new CPPPInvokeLoweredType(CPPPInvokeValueKind.Void, "void", null),
            Array.Empty<CPPPInvokeParameter>(),
            CPPPInvokeCallingConvention.StdCall);
        CPPPInvokeImport user32 = new CPPPInvokeImport("user32", "GetForegroundWindow", signature);
        user32.MethodIds.Add("M:A.GetForegroundWindow");
        CPPPInvokeImport kernel32 = new CPPPInvokeImport("kernel32", "GetTickCount64", signature);
        kernel32.MethodIds.Add("M:A.GetTickCount64");
        CPPPInvokeImport user32Again = new CPPPInvokeImport("user32", "GetDesktopWindow", signature);
        user32Again.MethodIds.Add("M:B.GetDesktopWindow");

        CPPPInvokePlan plan = new CPPPInvokePlan(new[] { user32, kernel32, user32Again }, Array.Empty<CPPPInvokeCallback>(), Array.Empty<CPPPInvokeMirrorStruct>());

        Assert.Equal(new[] { "kernel32", "user32" }, plan.LinkLibraries);
        Assert.True(plan.HasNativeImports);
        Assert.True(plan.TryGetImport("M:B.GetDesktopWindow", out CPPPInvokeImport resolved));
        Assert.Equal("he_pinvoke::user32::GetDesktopWindow", resolved.ForwarderQualifiedName);
        Assert.False(plan.TryGetImport("M:C.Missing", out _));
    }

    /// <summary>
    /// Ensures an empty plan reports no native imports and no link libraries.
    /// </summary>
    [Fact]
    public void Plan_Empty_HasNoNativeImports() {
        CPPPInvokePlan plan = new CPPPInvokePlan(Array.Empty<CPPPInvokeImport>(), Array.Empty<CPPPInvokeCallback>(), Array.Empty<CPPPInvokeMirrorStruct>());
        Assert.False(plan.HasNativeImports);
        Assert.Empty(plan.LinkLibraries);
    }
}
