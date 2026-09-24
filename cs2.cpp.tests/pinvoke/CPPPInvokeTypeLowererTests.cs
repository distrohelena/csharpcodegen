using cs2.cpp;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests.pinvoke;

/// <summary>
/// Verifies which C# types may cross a direct P/Invoke boundary and the native mirror text each one lowers to.
/// </summary>
public sealed class CPPPInvokeTypeLowererTests {
    /// <summary>
    /// Shared fixture source declaring every type the tests lower.
    /// </summary>
    const string Source = """
        using System.Runtime.InteropServices;
        namespace Native {
            public enum ShowCommand : uint { Hide = 0, Show = 5 }
            public struct NativePoint { public int X; public int Y; }
            public struct NativeRect { public NativePoint TopLeft; public NativePoint BottomRight; }
            [StructLayout(LayoutKind.Explicit)] public struct Overlay { [FieldOffset(0)] public int A; [FieldOffset(0)] public float B; }
            public unsafe struct WithFixed { public fixed byte Name[8]; }
            public struct Generic<T> { public T Value; }
            public unsafe static class Probe {
                public static void Primitives(sbyte a, byte b, short c, ushort d, int e, uint f, long g, ulong h, float i, double j, nint k, nuint l) {}
                public static void Enums(ShowCommand command) {}
                public static void Pointers(int* a, void* b, NativePoint* c) {}
                public static void Structs(NativePoint p, NativeRect r, ref NativePoint rp, out NativePoint op, ref Overlay ro, Overlay vo) { op = default; }
                public static void Rejected(bool a, char b, string c, int[] d, object e, WithFixed f, Generic<int> g, in NativePoint h) {}
                public static void Pointers2(delegate* unmanaged[Stdcall]<nint, uint, nint> a, delegate* unmanaged[Cdecl]<int, void> b, delegate* unmanaged<int> c, delegate*<int> d) {}
            }
        }
        """;

    /// <summary>
    /// Lowers one parameter of a Probe method.
    /// </summary>
    static CPPPInvokeTypeLoweringResult LowerParameter(CPPPInvokeTypeLowerer lowerer, string method, int index, bool isCallback = false) {
        CSharpCompilation compilation = CPPPInvokeTestCompilation.Create(Source);
        IParameterSymbol parameter = CPPPInvokeTestCompilation.GetMethod(compilation, "Native.Probe", method).Parameters[index];
        return lowerer.LowerParameter(parameter.Type, parameter.RefKind, isCallback);
    }

    /// <summary>
    /// Ensures every blittable primitive maps to its fixed-width C type.
    /// </summary>
    [Fact]
    public void LowerParameter_Primitives_MapToFixedWidthTypes() {
        string[] expected = { "int8_t", "uint8_t", "int16_t", "uint16_t", "int32_t", "uint32_t", "int64_t", "uint64_t", "float", "double", "intptr_t", "uintptr_t" };
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        for (int index = 0; index < expected.Length; index++) {
            CPPPInvokeTypeLoweringResult result = LowerParameter(lowerer, "Primitives", index);
            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(CPPPInvokeValueKind.Primitive, result.Type.Kind);
            Assert.Equal(expected[index], result.Type.MirrorTypeText);
        }
    }

    /// <summary>
    /// Ensures enums lower through their underlying integer type.
    /// </summary>
    [Fact]
    public void LowerParameter_Enum_UsesUnderlyingType() {
        CPPPInvokeTypeLoweringResult result = LowerParameter(new CPPPInvokeTypeLowerer(), "Enums", 0);
        Assert.Equal(CPPPInvokeValueKind.Enum, result.Type.Kind);
        Assert.Equal("uint32_t", result.Type.MirrorTypeText);
    }

    /// <summary>
    /// Ensures every pointer crosses as void*, pointers to primitives and void record no mirror, and a pointer to a
    /// source struct records a layout-check mirror for its pointee.
    /// </summary>
    [Fact]
    public void LowerParameter_Pointers_BecomeVoidPointer_StructPointeeGetsLayoutMirror() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        for (int index = 0; index < 2; index++) {
            CPPPInvokeTypeLoweringResult result = LowerParameter(lowerer, "Pointers", index);
            Assert.Equal(CPPPInvokeValueKind.Pointer, result.Type.Kind);
            Assert.Equal("void*", result.Type.MirrorTypeText);
        }
        Assert.Empty(lowerer.MirrorStructs);

        CPPPInvokeTypeLoweringResult structPointer = LowerParameter(lowerer, "Pointers", 2);
        Assert.Equal(CPPPInvokeValueKind.Pointer, structPointer.Type.Kind);
        Assert.Equal("void*", structPointer.Type.MirrorTypeText);
        Assert.Equal(new[] { "he_pinvoke_Native_NativePoint" }, lowerer.MirrorStructs.Select(mirror => mirror.MirrorName));
    }

    /// <summary>
    /// Ensures by-value structs create mirrors with nested mirrors first, and structs crossing by ref also record a
    /// layout mirror (an explicit-layout one carrying each field's FieldOffset) so their layout is asserted too.
    /// </summary>
    [Fact]
    public void LowerParameter_Structs_MirrorsNestedFirst_ByRefRecordsLayoutMirror() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Assert.Equal("he_pinvoke_Native_NativePoint", LowerParameter(lowerer, "Structs", 0).Type.MirrorTypeText);
        Assert.Equal("he_pinvoke_Native_NativeRect", LowerParameter(lowerer, "Structs", 1).Type.MirrorTypeText);
        Assert.True(LowerParameter(lowerer, "Structs", 2).Succeeded);
        Assert.True(LowerParameter(lowerer, "Structs", 3).Succeeded);
        Assert.True(LowerParameter(lowerer, "Structs", 4).Succeeded);

        Assert.Equal(new[] { "he_pinvoke_Native_NativePoint", "he_pinvoke_Native_NativeRect", "he_pinvoke_Native_Overlay" }, lowerer.MirrorStructs.Select(mirror => mirror.MirrorName));
        CPPPInvokeMirrorStruct rect = lowerer.MirrorStructs[1];
        Assert.False(rect.IsExplicitLayout);
        Assert.Equal(new[] { "TopLeft", "BottomRight" }, rect.Fields.Select(field => field.Name));
        Assert.Equal("he_pinvoke_Native_NativePoint", rect.Fields[0].MirrorTypeText);

        CPPPInvokeMirrorStruct overlay = lowerer.MirrorStructs[2];
        Assert.True(overlay.IsExplicitLayout);
        Assert.Equal(new[] { 0, 0 }, overlay.Fields.Select(field => field.ExplicitOffset));
    }

    /// <summary>
    /// Ensures a struct that only crosses by ref still records a layout mirror.
    /// </summary>
    [Fact]
    public void LowerParameter_StructByRefOnly_RecordsLayoutMirror() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Assert.True(LowerParameter(lowerer, "Structs", 3).Succeeded);
        Assert.Equal(new[] { "he_pinvoke_Native_NativePoint" }, lowerer.MirrorStructs.Select(mirror => mirror.MirrorName));
    }

    /// <summary>
    /// Ensures an explicit-layout struct is rejected by value.
    /// </summary>
    [Fact]
    public void LowerParameter_ExplicitStructByValue_Fails() {
        CPPPInvokeTypeLoweringResult result = LowerParameter(new CPPPInvokeTypeLowerer(), "Structs", 5);
        Assert.False(result.Succeeded);
        Assert.Contains("sequential", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures each non-blittable or marshalled type is rejected with a recommendation.
    /// </summary>
    [Theory]
    [InlineData(0, "bool")]
    [InlineData(1, "char")]
    [InlineData(2, "marshalling")]
    [InlineData(3, "marshalling")]
    [InlineData(4, "marshalling")]
    [InlineData(5, "fixed-size buffers")]
    [InlineData(6, "marshalling")]
    [InlineData(7, "rvalue")]
    public void LowerParameter_Rejected_FailsWithReason(int index, string reasonFragment) {
        CPPPInvokeTypeLoweringResult result = LowerParameter(new CPPPInvokeTypeLowerer(), "Rejected", index);
        Assert.False(result.Succeeded);
        Assert.Contains(reasonFragment, result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(result.Recommendation));
    }

    /// <summary>
    /// Ensures unmanaged function pointers keep their calling convention and managed ones are rejected.
    /// </summary>
    [Fact]
    public void LowerParameter_FunctionPointers_CarryCallingConvention() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Assert.Equal("intptr_t (HE_CPP_STDCALL*)(intptr_t,uint32_t)", LowerParameter(lowerer, "Pointers2", 0).Type.MirrorTypeText);
        Assert.Equal("void (HE_CPP_CDECL*)(int32_t)", LowerParameter(lowerer, "Pointers2", 1).Type.MirrorTypeText);
        Assert.Equal("int32_t (HE_CPP_STDCALL*)()", LowerParameter(lowerer, "Pointers2", 2).Type.MirrorTypeText);
        CPPPInvokeTypeLoweringResult managed = LowerParameter(lowerer, "Pointers2", 3);
        Assert.False(managed.Succeeded);
        Assert.Contains("managed function pointers", managed.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures callbacks reject structs by value.
    /// </summary>
    [Fact]
    public void LowerParameter_CallbackStructByValue_Fails() {
        CPPPInvokeTypeLoweringResult result = LowerParameter(new CPPPInvokeTypeLowerer(), "Structs", 0, isCallback: true);
        Assert.False(result.Succeeded);
        Assert.Contains("callbacks", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fixture source declaring the struct shapes whose layout cannot be verified, or that are verified only through
    /// pointers, for the layout-verification tests.
    /// </summary>
    const string LayoutSource = """
        using System;
        using System.Runtime.InteropServices;
        namespace Native {
            public struct AutoProperty { public int X { get; set; } }
            public record struct Positional(int X, int Y);
            [StructLayout(LayoutKind.Sequential, Size = 16)] public struct Sized { public int X; }
            public struct KeywordField { public int union; }
            [StructLayout(LayoutKind.Auto)] public struct AutoLayout { public int X; }
            [StructLayout(LayoutKind.Explicit)] public struct Overlay { [FieldOffset(0)] public int A; [FieldOffset(4)] public float B; }
            public struct ContainsOverlay { public Overlay Inner; }
            public struct WithBool { public bool Flag; public int X; }
            public unsafe struct Node { public Node* Next; public int Value; }
            public unsafe struct Holder { public WithBool* Flags; public char* Text; }
            public unsafe static class Probe {
                public static void ByValue(AutoProperty a, Positional b, Sized c, KeywordField d, Guid e, Node f, Holder g) {}
                public static void ByRef(ref AutoProperty a, ref Sized b, ref Guid c, ref AutoLayout d, ref ContainsOverlay e, ref Overlay f) {}
                public static void Pointers(AutoProperty* a, Sized* b, Guid* c, WithBool* d, bool* e, char* f, AutoLayout* g) {}
            }
        }
        """;

    /// <summary>
    /// Lowers one parameter of a <c>Native.Probe</c> method declared in <see cref="LayoutSource"/>.
    /// </summary>
    static CPPPInvokeTypeLoweringResult LowerLayoutParameter(CPPPInvokeTypeLowerer lowerer, string method, int index) {
        CSharpCompilation compilation = CPPPInvokeTestCompilation.Create(LayoutSource);
        IParameterSymbol parameter = CPPPInvokeTestCompilation.GetMethod(compilation, "Native.Probe", method).Parameters[index];
        return lowerer.LowerParameter(parameter.Type, parameter.RefKind, false);
    }

    /// <summary>
    /// Ensures structs whose generated layout cannot be verified are rejected by value, by ref and through a pointer:
    /// compiler-generated backing fields, StructLayout.Size (reported as an unsupported setting), and structs that are
    /// not declared in source.
    /// </summary>
    [Theory]
    [InlineData("ByValue", 0, "hidden fields", false)]
    [InlineData("ByValue", 1, "hidden fields", false)]
    [InlineData("ByValue", 2, "StructLayout.Size is not supported", true)]
    [InlineData("ByValue", 4, "not declared in source", false)]
    [InlineData("ByRef", 0, "hidden fields", false)]
    [InlineData("ByRef", 1, "StructLayout.Size is not supported", true)]
    [InlineData("ByRef", 2, "not declared in source", false)]
    [InlineData("Pointers", 0, "hidden fields", false)]
    [InlineData("Pointers", 1, "StructLayout.Size is not supported", true)]
    [InlineData("Pointers", 2, "not declared in source", false)]
    public void LowerParameter_UnverifiableStructLayout_Fails(string method, int index, string reasonFragment, bool isUnsupportedSetting) {
        CPPPInvokeTypeLoweringResult result = LowerLayoutParameter(new CPPPInvokeTypeLowerer(), method, index);
        Assert.False(result.Succeeded);
        Assert.Contains(reasonFragment, result.FailureReason, StringComparison.Ordinal);
        Assert.Equal(isUnsupportedSetting, result.IsUnsupportedSetting);
        Assert.False(string.IsNullOrWhiteSpace(result.Recommendation));
    }

    /// <summary>
    /// Ensures the backing-field and non-source rejections carry the ruled recommendations.
    /// </summary>
    [Fact]
    public void LowerParameter_UnverifiableStructLayout_RecommendsSourceFix() {
        Assert.Equal("declare explicit fields", LowerLayoutParameter(new CPPPInvokeTypeLowerer(), "ByValue", 0).Recommendation);
        Assert.Equal("declare an equivalent struct in source", LowerLayoutParameter(new CPPPInvokeTypeLowerer(), "ByValue", 4).Recommendation);
    }

    /// <summary>
    /// Ensures a field whose name is a C++ keyword is rejected, because the generated struct and its mirror could not
    /// name the same member.
    /// </summary>
    [Fact]
    public void LowerParameter_KeywordFieldName_Fails() {
        CPPPInvokeTypeLoweringResult result = LowerLayoutParameter(new CPPPInvokeTypeLowerer(), "ByValue", 3);
        Assert.False(result.Succeeded);
        Assert.Contains("C++ keyword", result.FailureReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures auto-layout structs by ref and explicit-layout structs nested inside another crossing struct are
    /// rejected, since neither layout can be mirrored and asserted.
    /// </summary>
    [Theory]
    [InlineData(3, "auto-layout")]
    [InlineData(4, "nested")]
    public void LowerParameter_UnmirrorableByRefLayout_Fails(int index, string reasonFragment) {
        CPPPInvokeTypeLoweringResult result = LowerLayoutParameter(new CPPPInvokeTypeLowerer(), "ByRef", index);
        Assert.False(result.Succeeded);
        Assert.Contains(reasonFragment, result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures an explicit-layout struct by ref records each field's FieldOffset for the layout assertions.
    /// </summary>
    [Fact]
    public void LowerParameter_ExplicitStructByRef_RecordsFieldOffsets() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Assert.True(LowerLayoutParameter(lowerer, "ByRef", 5).Succeeded);
        CPPPInvokeMirrorStruct overlay = Assert.Single(lowerer.MirrorStructs);
        Assert.True(overlay.IsExplicitLayout);
        Assert.Equal(new[] { "A", "B" }, overlay.Fields.Select(field => field.Name));
        Assert.Equal(new[] { 0, 4 }, overlay.Fields.Select(field => field.ExplicitOffset));
        Assert.Equal(new[] { "int32_t", "float" }, overlay.Fields.Select(field => field.MirrorTypeText));
    }

    /// <summary>
    /// Ensures pointers are never marshalled: pointers to bool, char, and to structs whose fields cannot be mirrored
    /// (bool fields, auto layout) are accepted, and such pointee structs get no layout mirror.
    /// </summary>
    [Fact]
    public void LowerParameter_PointerToUnmirrorablePointee_SucceedsWithoutMirror() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        for (int index = 3; index < 7; index++) {
            CPPPInvokeTypeLoweringResult result = LowerLayoutParameter(lowerer, "Pointers", index);
            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal("void*", result.Type.MirrorTypeText);
        }
        Assert.Empty(lowerer.MirrorStructs);
    }

    /// <summary>
    /// Ensures a by-value struct whose pointer fields point at unmirrorable structs, at char, or back at itself lowers
    /// without infinite recursion, and only the mirrorable structs get mirrors.
    /// </summary>
    [Fact]
    public void LowerParameter_StructWithSelfAndUnmirrorablePointers_Succeeds() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Assert.True(LowerLayoutParameter(lowerer, "ByValue", 5).Succeeded);
        Assert.True(LowerLayoutParameter(lowerer, "ByValue", 6).Succeeded);
        Assert.Equal(new[] { "he_pinvoke_Native_Node", "he_pinvoke_Native_Holder" }, lowerer.MirrorStructs.Select(mirror => mirror.MirrorName));
        Assert.Equal(new[] { "void*", "int32_t" }, lowerer.MirrorStructs[0].Fields.Select(field => field.MirrorTypeText));
    }
}
