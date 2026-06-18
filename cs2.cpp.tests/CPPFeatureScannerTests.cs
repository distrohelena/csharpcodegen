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
    public void Scan_WhenShaderRuntimeTypesAreReferenced_DetectsShaders() {
        string source = """
namespace ExampleEngine {
    public class ShaderAsset {
    }

    public interface IShaderRenderManager3D {
    }
}

namespace SampleGame {
    using ExampleEngine;

    public class MaterialHost {
        public ShaderAsset Asset { get; set; }
    }

    public class ShaderRendererHost {
        public IShaderRenderManager3D Renderer { get; set; }
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation, catalog).ToArray();

        Assert.Contains(roots, root => root.FeatureId == "shaders");
    }

    /// <summary>
    /// Verifies that sprite-oriented interfaces enable both sprite and render2d feature buckets.
    /// </summary>
    [Fact]
    public void Scan_WhenSpriteInterfaceIsReferenced_DetectsSpritesAndRender2D() {
        string source = """
namespace ExampleEngine.Core.Graphics {
    public interface ISpriteRenderable {
    }
}

namespace SampleGame {
    using ExampleEngine.Core.Graphics;

    public class SpriteCard : ISpriteRenderable {
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation, catalog).ToArray();

        Assert.Contains(roots, root => root.FeatureId == "sprites");
        Assert.Contains(roots, root => root.FeatureId == "render2d");
    }

    /// <summary>
    /// Verifies that the real text component roots enable both text and shared 2D rendering buckets.
    /// </summary>
    [Fact]
    public void Scan_WhenTextComponentIsReferenced_DetectsText2DAndRender2D() {
        string source = """
namespace ExampleEngine {
    public interface IDrawable2D {
    }

    public interface ITextDrawable2D : IDrawable2D {
    }

    public class TextComponent : ITextDrawable2D {
    }
}

namespace SampleGame {
    using ExampleEngine;

    public class HudLabel {
        public TextComponent Value { get; set; }
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation, catalog).ToArray();

        Assert.Contains(roots, root => root.FeatureId == "text2d");
        Assert.Contains(roots, root => root.FeatureId == "render2d");
    }

    /// <summary>
    /// Verifies that the real debug overlay component root enables the debug overlay bucket.
    /// </summary>
    [Fact]
    public void Scan_WhenDebugOverlayComponentIsReferenced_DetectsDebugOverlay() {
        string source = """
namespace ExampleEngine {
    public class DebugOverlayComponent {
    }
}

namespace SampleGame {
    using ExampleEngine;

    public class DebugScreen {
        public DebugOverlayComponent Overlay { get; set; }
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation, catalog).ToArray();

        Assert.Contains(roots, root => root.FeatureId == "debug_overlay");
    }

    /// <summary>
    /// Verifies that the high-level debug component root enables the debug overlay bucket because it creates overlay UI at runtime.
    /// </summary>
    [Fact]
    public void Scan_WhenDebugComponentIsReferenced_DetectsDebugOverlay() {
        string source = """
namespace ExampleEngine {
    public class DebugComponent {
    }
}

namespace SampleGame {
    using ExampleEngine;

    public class DebugScreen {
        public DebugComponent Overlay { get; set; }
    }
}
""";

        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(source);
        CPPExternalFeatureCatalog catalog = CPPTestFeatureCatalogFactory.CreateSampleFeatureCatalog();

        CPPFeatureUsageRoot[] roots = CPPFeatureScanner.Scan(compilation, catalog).ToArray();

        Assert.Contains(roots, root => root.FeatureId == "debug_overlay");
    }
}
