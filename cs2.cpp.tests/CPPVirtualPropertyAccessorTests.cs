using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that a property carries its declared modifiers into the generated accessor.
/// </summary>
/// <remarks>
/// A getter with an expression body is emitted by a different path from one with a
/// block, and that path used to write only the static keyword. A property declared
/// virtual therefore lowered to a non-virtual accessor, which no subclass outside the
/// generated program could override: platform renderers, which live outside it, could
/// not replace the engine's default and the engine silently read the default forever.
/// </remarks>
public sealed class CPPVirtualPropertyAccessorTests {
    /// <summary>
    /// Ensures a virtual property with an expression body generates a virtual accessor.
    /// </summary>
    [Fact]
    public void Convert_WhenVirtualPropertyHasExpressionBody_EmitsVirtualAccessor() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(Convert_WhenVirtualPropertyHasExpressionBody_EmitsVirtualAccessor),
            """
            public class Fixture {
                public virtual int DrawCallCount => 0;
            }
            """);

        string header = ReadHeader(output, "Fixture");

        Assert.Contains("virtual int32_t get_DrawCallCount();", header);
    }

    /// <summary>
    /// Ensures the same property with a block body keeps generating a virtual accessor,
    /// so the two emission paths agree.
    /// </summary>
    [Fact]
    public void Convert_WhenVirtualPropertyHasBlockBody_EmitsVirtualAccessor() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(Convert_WhenVirtualPropertyHasBlockBody_EmitsVirtualAccessor),
            """
            public class Fixture {
                public virtual int DrawCallCount {
                    get { return 0; }
                }
            }
            """);

        string header = ReadHeader(output, "Fixture");

        Assert.Contains("virtual int32_t get_DrawCallCount();", header);
    }

    /// <summary>
    /// Ensures a property that is not virtual does not gain a vtable slot it never
    /// asked for, which matters on targets where every slot is counted.
    /// </summary>
    [Fact]
    public void Convert_WhenPropertyIsNotVirtual_EmitsPlainAccessor() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(Convert_WhenPropertyIsNotVirtual_EmitsPlainAccessor),
            """
            public class Fixture {
                public int DrawCallCount => 0;
            }
            """);

        string header = ReadHeader(output, "Fixture");

        Assert.Contains("int32_t get_DrawCallCount();", header);
        Assert.DoesNotContain("virtual int32_t get_DrawCallCount();", header);
    }

    /// <summary>
    /// Ensures a static expression-bodied property keeps its static keyword and gains no
    /// virtual, since the two cannot be combined.
    /// </summary>
    [Fact]
    public void Convert_WhenPropertyIsStatic_EmitsStaticAccessorWithoutVirtual() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(Convert_WhenPropertyIsStatic_EmitsStaticAccessorWithoutVirtual),
            """
            public class Fixture {
                public static int DrawCallCount => 0;
            }
            """);

        string header = ReadHeader(output, "Fixture");

        Assert.Contains("static int32_t get_DrawCallCount();", header);
        Assert.DoesNotContain("virtual", header.Substring(header.IndexOf("get_DrawCallCount", StringComparison.Ordinal) - 40));
    }

    /// <summary>
    /// Reads one generated header from a completed conversion.
    /// </summary>
    /// <param name="output">Conversion output to read from.</param>
    /// <param name="typeName">Generated type whose header is wanted.</param>
    /// <returns>The header text.</returns>
    static string ReadHeader(CPPOwnershipConversionOutput output, string typeName) {
        string headerPath = Path.Combine(output.OutputPath, typeName + ".hpp");
        Assert.True(File.Exists(headerPath), "expected a generated header at " + headerPath);
        return File.ReadAllText(headerPath);
    }
}
