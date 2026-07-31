using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Locks the text-effect pass ownership shape that originally leaked a factory-produced native list.
/// </summary>
public sealed class CPPTextRenderEffectOwnershipRegressionTests {
    /// <summary>
    /// Ensures the factory transfers its list and the conditional caller local performs exactly one guarded deletion.
    /// </summary>
    [Fact]
    public void WriteOutput_WithConditionalTextEffectPasses_TransfersFactoryAndDeletesCallerLocal() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(WriteOutput_WithConditionalTextEffectPasses_TransfersFactoryAndDeletesCallerLocal),
            """
            using System.Collections.Generic;

            public interface ITextDrawable2D {
                bool Enabled { get; }
            }

            public sealed class TextRenderEffectPass {
            }

            public static class TextRenderEffectPassBuilder {
                public static List<TextRenderEffectPass> Build(ITextDrawable2D drawable) {
                    List<TextRenderEffectPass> passes = new List<TextRenderEffectPass>(6);
                    passes.Add(new TextRenderEffectPass());
                    return passes;
                }
            }

            public sealed class RenderCommandListBuilder2D {
                public void EmitText(ITextDrawable2D text) {
                    bool hasTextEffects = text.Enabled;
                    List<TextRenderEffectPass> effectPasses = hasTextEffects
                        ? TextRenderEffectPassBuilder.Build(text)
                        : null;

                    if (hasTextEffects) {
                        Use(effectPasses.Count);
                    }
                }

                static void Use(int count) {
                }
            }
            """);
        string factoryOutput = File.ReadAllText(Path.Combine(output.OutputPath, "TextRenderEffectPassBuilder.cpp"));
        string callerOutput = File.ReadAllText(Path.Combine(output.OutputPath, "RenderCommandListBuilder2D.cpp"));

        Assert.Contains("return passes;", factoryOutput, StringComparison.Ordinal);
        Assert.Matches("__owns_passes_[0-9]+ = false;", factoryOutput);
        Assert.Equal(1, CountOccurrences(factoryOutput, "delete passes;"));
        Assert.Contains("bool __owns_effectPasses_", callerOutput, StringComparison.Ordinal);
        Assert.Contains("if (__owns_effectPasses_", callerOutput, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(callerOutput, "delete effectPasses;"));
    }

    /// <summary>
    /// Counts non-overlapping occurrences of one fragment in generated source.
    /// </summary>
    /// <param name="text">Generated source text to inspect.</param>
    /// <param name="fragment">Non-empty fragment whose occurrences are counted.</param>
    /// <returns>The number of non-overlapping fragment occurrences.</returns>
    static int CountOccurrences(string text, string fragment) {
        if (string.IsNullOrEmpty(fragment)) {
            throw new ArgumentException("A generated-source fragment is required.", nameof(fragment));
        }

        int count = 0;
        int searchStart = 0;
        while (searchStart < text.Length) {
            int fragmentIndex = text.IndexOf(fragment, searchStart, StringComparison.Ordinal);
            if (fragmentIndex < 0) {
                break;
            }

            count++;
            searchStart = fragmentIndex + fragment.Length;
        }

        return count;
    }
}
