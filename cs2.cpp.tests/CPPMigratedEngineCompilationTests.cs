using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Compiles and executes native regressions found while building the migrated engine and DemoDisc.
/// </summary>
public sealed class CPPMigratedEngineCompilationTests {
    /// <summary>
    /// Verifies chained string and character replacements return durable values from computed getters.
    /// </summary>
    [Fact]
    public void ComputedStringReplacement_ReturnsValuesWithoutDanglingReferences() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(ComputedStringReplacement_ReturnsValuesWithoutDanglingReferences),
            """
            public sealed class TextureFormat {
                public string Id = "Gx.RGBA.TLUT.RGB";
                public string DisplayName => Id.Replace("Gx.", "GX ").Replace(".TLUT.", " / TLUT ");
                public string Characters => Id.Replace('.', '/');
            }
            """);
        string header = File.ReadAllText(Path.Combine(output.OutputPath, "TextureFormat.hpp"));
        Assert.Contains("std::string get_DisplayName();", header);
        Assert.DoesNotContain(".Replace(", output.GeneratedText);
        AssertProgram(output, """
            TextureFormat format;
            auto text = format.get_DisplayName();
            format.Id = "changed";
            if (text != "GX RGBA / TLUT RGB") return 1;
            format.Id = "Gx.RGBA.TLUT.RGB";
            if (format.get_Characters() != "Gx/RGBA/TLUT/RGB") return 2;
            """);
    }

    /// <summary>
    /// Verifies stored and computed strings share a value-return signature across interfaces and virtual overrides.
    /// </summary>
    [Fact]
    public void PolymorphicStringGetters_KeepCompatibleValueSignatures() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(PolymorphicStringGetters_KeepCompatibleValueSignatures),
            """
            public interface ILabel {
                string Text { get; }
            }
            public class StoredLabel : ILabel {
                public string Text { get; set; } = "stored";
                public virtual string Caption => Text;
            }
            public sealed class ComputedLabel : StoredLabel {
                public override string Caption => Text.Replace("stored", "computed");
            }
            public sealed class ComputedInterfaceLabel : ILabel {
                public string Text => "input".Replace("input", "output");
            }
            """);
        AssertProgram(output, """
            StoredLabel stored;
            ILabel* storedView = &stored;
            if (storedView->get_Text() != "stored") return 1;
            ComputedInterfaceLabel computed;
            ILabel* computedView = &computed;
            if (computedView->get_Text() != "output") return 2;
            ComputedLabel derived;
            StoredLabel* virtualView = &derived;
            if (virtualView->get_Caption() != "computed") return 3;
            """);
    }

    /// <summary>
    /// Verifies instance and static expression getters provide a valid function context for lazy pointer coalescing.
    /// </summary>
    [Fact]
    public void ArrayCoalescingGetters_CaptureInstanceAndPreserveStoredValues() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(ArrayCoalescingGetters_CaptureInstanceAndPreserveStoredValues),
            """
            using System;
            using cs2.attributes;
            public sealed class SceneData {
                public int[] ValuesValue;
                public static int[] StaticValue;
                [NativeBorrowedReturn]
                public int[] Values => ValuesValue ?? Array.Empty<int>();
                [NativeBorrowedReturn]
                public static int[] StaticValues => StaticValue ?? Array.Empty<int>();
            }
            """);
        AssertProgram(output, """
            SceneData data;
            if (data.get_Values()->Length != 0) return 1;
            if (SceneData::get_StaticValues()->Length != 0) return 2;
            Array<int32_t> values(1);
            values[0] = 42;
            data.ValuesValue = &values;
            SceneData::StaticValue = &values;
            if (data.get_Values() != &values || SceneData::get_StaticValues() != &values) return 3;
            """);
    }

    /// <summary>
    /// Verifies parameter, explicit local, inferred local and loop identifiers use the same C++ keyword escaping.
    /// </summary>
    [Fact]
    public void CppKeywordIdentifiers_CompileAndKeepReferencesConsistent() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(CppKeywordIdentifiers_CompileAndKeepReferencesConsistent),
            """
            public sealed class Codec {
                public int Decode(bool signed, in int concept) {
                    long unsigned = signed ? -2 : 2;
                    var alignas = concept;
                    for (int template = 0; template < 2; template++) {
                        unsigned += template;
                    }
                    return (int)unsigned + alignas;
                }
            }
            """);
        AssertProgram(output, """
            Codec codec;
            int32_t value = 10;
            if (codec.Decode(true, value) != 9) return 1;
            if (codec.Decode(false, value) != 13) return 2;
            """);
    }

    /// <summary>
    /// Verifies detaching array elements preserves storage and evaluates a borrowed getter exactly once.
    /// </summary>
    [Fact]
    public void DetachOwnedArray_PreservesStorageAndGetterEvaluation() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(DetachOwnedArray_PreservesStorageAndGetterEvaluation),
            """
            using cs2.attributes;
            public static class NativeOwnership {
                public static void DetachOwned<T>([NativeNoEscape] T value) where T : class { }
            }
            public sealed class Catalog {
                public int Calls;
                public int[] Storage;
                [NativeBorrowedReturn]
                public int[] Values {
                    get { Calls++; return Storage; }
                }
                public void Detach() {
                    NativeOwnership.DetachOwned(Values);
                }
            }
            """);
        Assert.DoesNotContain("->DetachOwned()", output.GeneratedText);
        AssertProgram(output, """
            Catalog catalog;
            Array<int32_t> values(1);
            values[0] = 7;
            catalog.Storage = &values;
            catalog.Detach();
            if (catalog.Calls != 1 || catalog.Storage != &values || values[0] != 7) return 1;
            """);
    }

    /// <summary>
    /// Verifies checked arrow getters emit operand temporaries, evaluate each operand once and retain overflow failures.
    /// </summary>
    [Fact]
    public void CheckedExpressionGetter_EmitsTemporariesAndPreservesOverflow() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(CheckedExpressionGetter_EmitsTemporariesAndPreservesOverflow),
            """
            public sealed class TextureLayout {
                public int Calls;
                public int Limit = int.MaxValue;
                public int Size => checked(NextValue() * NextValue());
                public int Overflow => checked(Limit + 1);
                int NextValue() {
                    Calls++;
                    return Calls;
                }
            }
            """,
            new Dictionary<string, string> {
                ["codegen-use-exceptions"] = "true"
            });
        AssertProgram(output, """
            TextureLayout layout;
            if (layout.get_Size() != 2 || layout.Calls != 2) return 1;
            bool failed = false;
            try { layout.get_Overflow(); } catch (...) { failed = true; }
            if (!failed) return 2;
            """);
    }

    /// <summary>
    /// Verifies checked temporaries stay inside the selected ternary branch in constructors, methods and getters.
    /// </summary>
    [Fact]
    public void CheckedConditionalOperands_RemainLazyAndEvaluateOnce() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(CheckedConditionalOperands_RemainLazyAndEvaluateOnce),
            """
            public sealed class ConditionalLayout {
                public int Calls;
                public int Result;
                public int Limit = int.MaxValue;
                public bool Selected;
                public ConditionalLayout(bool selected) {
                    Result = selected ? checked(NextValue() + NextValue()) : 11;
                }
                public int Size => Selected ? checked(NextValue() + NextValue()) : 11;
                public int Overflow => Selected ? checked(Limit + 1) : 11;
                public int Compute(bool selected) {
                    return selected ? checked(NextValue() + NextValue()) : 11;
                }
                int NextValue() {
                    Calls++;
                    return Calls;
                }
            }
            """,
            new Dictionary<string, string> {
                ["codegen-use-exceptions"] = "true"
            });
        AssertProgram(output, """
            ConditionalLayout skipped(false);
            if (skipped.Result != 11 || skipped.Calls != 0) return 1;
            if (skipped.Compute(false) != 11 || skipped.get_Size() != 11 || skipped.get_Overflow() != 11) return 2;
            if (skipped.Calls != 0) return 3;
            ConditionalLayout selected(true);
            if (selected.Result != 3 || selected.Calls != 2) return 4;
            selected.Selected = true;
            if (selected.get_Size() != 7 || selected.Calls != 4) return 5;
            if (selected.Compute(true) != 11 || selected.Calls != 6) return 6;
            bool failed = false;
            try { selected.get_Overflow(); } catch (...) { failed = true; }
            if (!failed) return 7;
            """);
    }

    /// <summary>
    /// Verifies ternary reference selection retains aliasing when the chosen result is mutated by native callers.
    /// </summary>
    [Fact]
    public void RefConditionalOperands_PreserveSelectedStorage() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(RefConditionalOperands_PreserveSelectedStorage),
            """
            public sealed class ReferenceSelection {
                public int Left = 1;
                public int Right = 2;
                public ref int Pick(bool useLeft) {
                    ref var selected = ref useLeft ? ref Left : ref Right;
                    return ref selected;
                }
            }
            """);
        AssertProgram(output, """
            ReferenceSelection selection;
            selection.Pick(true) = 7;
            selection.Pick(false) = 9;
            if (selection.Left != 7 || selection.Right != 9) return 1;
            """);
    }

    /// <summary>
    /// Verifies checked additions retain operand sequencing inside array lengths, casts and invocation arguments.
    /// </summary>
    [Fact]
    public void NestedCheckedAddition_PreservesTemporariesInExpressionContainers() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(NestedCheckedAddition_PreservesTemporariesInExpressionContainers),
            """
            using cs2.attributes;
            public sealed class ArrayLayout {
                public int Calls;
                public int Limit = 4;
                [NativeOwnedReturn]
                public byte[] Allocate() {
                    return new byte[checked(Limit + Padding())];
                }
                public long CastSum() {
                    return (long)checked(Limit + Padding());
                }
                public int ArgumentSum() {
                    return Identity(checked(Limit + Padding()));
                }
                int Padding() {
                    Calls++;
                    return 2;
                }
                static int Identity(int value) {
                    return value;
                }
            }
            """,
            new Dictionary<string, string> {
                ["codegen-use-exceptions"] = "true"
            });
        AssertProgram(output, """
            ArrayLayout layout;
            auto values = layout.Allocate();
            if (values->Length != 6 || layout.Calls != 1) return 1;
            delete values;
            if (layout.CastSum() != 6 || layout.Calls != 2) return 2;
            if (layout.ArgumentSum() != 6 || layout.Calls != 3) return 3;
            layout.Limit = 2147483647;
            bool failed = false;
            try { layout.Allocate(); } catch (...) { failed = true; }
            if (!failed || layout.Calls != 4) return 4;
            """);
    }

    /// <summary>
    /// Compiles a complete generated fixture and checks its native execution result.
    /// </summary>
    /// <param name="output">Generated fixture containing the engine regression.</param>
    /// <param name="body">Native assertions executed after including the generated unity source.</param>
    static void AssertProgram(CPPOwnershipConversionOutput output, string body) {
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run(body);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native exit code: {result.ProgramExitCode}. {result.ProgramOutput}");
    }
}
