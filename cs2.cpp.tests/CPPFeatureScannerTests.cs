using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Covers explicit phase-one feature detection from known code roots.
/// </summary>
public class CPPFeatureScannerTests {
    /// <summary>
    /// Verifies that shader-oriented type usage resolves to the shader feature bucket.
    /// </summary>
    [Fact]
    public void Scan_WhenShaderNamespaceIsReferenced_DetectsShaders() {
        string source = """
using helengine.core.shaders;

namespace helengine.core.shaders {
    public class ShaderAsset {
    }
}

namespace SampleGame {
    public class MaterialHost {
        public ShaderAsset Asset { get; set; }
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation).ToArray();

        Assert.Contains(roots, root => root.Feature == CPPFeatureKind.Shaders);
    }

    /// <summary>
    /// Verifies that sprite-oriented interfaces enable both sprite and render2d feature buckets.
    /// </summary>
    [Fact]
    public void Scan_WhenSpriteInterfaceIsReferenced_DetectsSpritesAndRender2D() {
        string source = """
namespace helengine.core.graphics {
    public interface ISpriteRenderable {
    }
}

namespace SampleGame {
    using helengine.core.graphics;

    public class SpriteCard : ISpriteRenderable {
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation).ToArray();

        Assert.Contains(roots, root => root.Feature == CPPFeatureKind.Sprites);
        Assert.Contains(roots, root => root.Feature == CPPFeatureKind.Render2D);
    }

    /// <summary>
    /// Verifies that the real text component roots enable both text and shared 2D rendering buckets.
    /// </summary>
    [Fact]
    public void Scan_WhenTextComponentIsReferenced_DetectsText2DAndRender2D() {
        string source = """
namespace helengine {
    public interface IDrawable2D {
    }

    public interface ITextDrawable2D : IDrawable2D {
    }

    public class TextComponent : ITextDrawable2D {
    }
}

namespace SampleGame {
    using helengine;

    public class HudLabel {
        public TextComponent Value { get; set; }
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation).ToArray();

        Assert.Contains(roots, root => root.Feature == CPPFeatureKind.Text2D);
        Assert.Contains(roots, root => root.Feature == CPPFeatureKind.Render2D);
    }

    /// <summary>
    /// Verifies that the real debug overlay component root enables the debug overlay bucket.
    /// </summary>
    [Fact]
    public void Scan_WhenDebugOverlayComponentIsReferenced_DetectsDebugOverlay() {
        string source = """
namespace helengine {
    public class DebugOverlayComponent {
    }
}

namespace SampleGame {
    using helengine;

    public class DebugScreen {
        public DebugOverlayComponent Overlay { get; set; }
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation).ToArray();

        Assert.Contains(roots, root => root.Feature == CPPFeatureKind.DebugOverlay);
    }
}
