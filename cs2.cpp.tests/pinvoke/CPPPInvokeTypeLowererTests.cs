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
    /// Ensures every pointer crosses as void* and records no mirror struct.
    /// </summary>
    [Fact]
    public void LowerParameter_Pointers_BecomeVoidPointerWithoutMirrors() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        for (int index = 0; index < 3; index++) {
            CPPPInvokeTypeLoweringResult result = LowerParameter(lowerer, "Pointers", index);
            Assert.Equal(CPPPInvokeValueKind.Pointer, result.Type.Kind);
            Assert.Equal("void*", result.Type.MirrorTypeText);
        }
        Assert.Empty(lowerer.MirrorStructs);
    }

    /// <summary>
    /// Ensures by-value structs create mirrors with nested mirrors first, while by-ref structs need none.
    /// </summary>
    [Fact]
    public void LowerParameter_Structs_ByValueMirrorsNestedFirst_ByRefNoMirror() {
        CPPPInvokeTypeLowerer lowerer = new CPPPInvokeTypeLowerer();
        Assert.Equal("he_pinvoke_Native_NativePoint", LowerParameter(lowerer, "Structs", 0).Type.MirrorTypeText);
        Assert.Equal("he_pinvoke_Native_NativeRect", LowerParameter(lowerer, "Structs", 1).Type.MirrorTypeText);
        Assert.True(LowerParameter(lowerer, "Structs", 2).Succeeded);
        Assert.True(LowerParameter(lowerer, "Structs", 3).Succeeded);
        Assert.True(LowerParameter(lowerer, "Structs", 4).Succeeded);

        Assert.Equal(new[] { "he_pinvoke_Native_NativePoint", "he_pinvoke_Native_NativeRect" }, lowerer.MirrorStructs.Select(mirror => mirror.MirrorName));
        CPPPInvokeMirrorStruct rect = lowerer.MirrorStructs[1];
        Assert.Equal(new[] { "TopLeft", "BottomRight" }, rect.Fields.Select(field => field.Name));
        Assert.Equal("he_pinvoke_Native_NativePoint", rect.Fields[0].MirrorTypeText);
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
}
