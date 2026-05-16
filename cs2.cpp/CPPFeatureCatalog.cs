using Microsoft.CodeAnalysis;

namespace cs2.cpp {
    /// <summary>
    /// Resolves explicit feature membership from known engine-facing root types.
    /// </summary>
    public static class CPPFeatureCatalog {
        static readonly Dictionary<string, CPPFeatureKind[]> TypeFeatureMap = new Dictionary<string, CPPFeatureKind[]>(StringComparer.Ordinal) {
            ["helengine.RenderManager2D"] = new[] { CPPFeatureKind.Render2D },
            ["helengine.IDrawable2D"] = new[] { CPPFeatureKind.Render2D },
            ["helengine.ISpriteDrawable2D"] = new[] { CPPFeatureKind.Sprites, CPPFeatureKind.Render2D },
            ["helengine.core.graphics.ISpriteRenderable"] = new[] { CPPFeatureKind.Sprites, CPPFeatureKind.Render2D },
            ["helengine.SpriteComponent"] = new[] { CPPFeatureKind.Sprites, CPPFeatureKind.Render2D },
            ["helengine.ITextDrawable2D"] = new[] { CPPFeatureKind.Text2D, CPPFeatureKind.Render2D },
            ["helengine.TextComponent"] = new[] { CPPFeatureKind.Text2D, CPPFeatureKind.Render2D },
            ["helengine.DebugOverlayComponent"] = new[] { CPPFeatureKind.DebugOverlay },
            ["helengine.core.shaders.compilation.ShaderConditionalPreprocessor"] = new[] { CPPFeatureKind.Shaders, CPPFeatureKind.TextProcessing },
            ["helengine.core.content.RuntimeManifestJsonReader"] = new[] { CPPFeatureKind.RuntimeJson, CPPFeatureKind.TextProcessing },
            ["helengine.TextContentProcessor"] = new[] { CPPFeatureKind.TextProcessing },
            ["helengine.core.content.RuntimeStartupManifest"] = new[] { CPPFeatureKind.RuntimeJson, CPPFeatureKind.HostFileSystem },
            ["helengine.core.content.RuntimeCodeModuleManifest"] = new[] { CPPFeatureKind.RuntimeJson, CPPFeatureKind.HostFileSystem },
            ["helengine.ContentManager"] = new[] { CPPFeatureKind.HostFileSystem, CPPFeatureKind.TextProcessing },
        };

        /// <summary>
        /// Tries to resolve one or more feature buckets from a Roslyn type symbol.
        /// </summary>
        /// <param name="symbol">Type symbol to classify.</param>
        /// <param name="features">Resolved feature buckets when the symbol is recognized.</param>
        /// <returns>True when the symbol belongs to one or more known feature buckets.</returns>
        public static bool TryGetFeatures(ITypeSymbol symbol, out CPPFeatureKind[] features) {
            features = Array.Empty<CPPFeatureKind>();
            if (symbol == null) {
                return false;
            }

            return TypeFeatureMap.TryGetValue(symbol.ToDisplayString(), out features);
        }
    }
}
