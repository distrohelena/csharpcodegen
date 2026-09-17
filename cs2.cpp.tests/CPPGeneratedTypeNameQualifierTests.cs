using System.Text.RegularExpressions;
using cs2.core;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Proves the single-pass qualifier matches the per-class regex replacement it replaces, including the rare case
/// where an emitted type name is not a pure identifier and the qualifier must fall back to the original regex loop.
/// </summary>
public sealed class CPPGeneratedTypeNameQualifierTests {
    /// <summary>
    /// The regex loop that <see cref="CPPGeneratedTypeNameQualifier"/> replaces, kept here as the behavioral oracle.
    /// </summary>
    static string ReferenceQualify(string renderedTypeName, IEnumerable<string> emittedTypeNames) {
        string qualifiedTypeName = renderedTypeName;
        foreach (string generatedTypeName in emittedTypeNames) {
            qualifiedTypeName = Regex.Replace(
                qualifiedTypeName,
                $@"(?<!:)\b{Regex.Escape(generatedTypeName)}\b",
                $"::{generatedTypeName}");
        }

        return qualifiedTypeName;
    }

    /// <summary>
    /// Creates a generated class registered on a fresh program, optionally with a <c>CodeGenRename</c> override so
    /// the emitted name can be forced to a non-identifier string.
    /// </summary>
    static ConversionClass CreateGeneratedClass(CPPProgram program, string name, string codeGenRename = null) {
        ConversionClass conversionClass = new ConversionClass {
            Name = name,
            IsNative = false,
            Program = program
        };
        if (codeGenRename != null) {
            conversionClass.CodeGenRename = codeGenRename;
        }

        program.Classes.Add(conversionClass);
        return conversionClass;
    }

    /// <summary>
    /// Builds an index over a fresh program populated with the given identifier-only class names.
    /// </summary>
    static CPPEmittedTypeNameIndex BuildIdentifierOnlyIndex(params string[] classNames) {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        foreach (string className in classNames) {
            CreateGeneratedClass(program, className);
        }

        return CPPEmittedTypeNameIndex.Build(program.Classes);
    }

    /// <summary>
    /// Rendered type strings covering generics, pointers, references, already-qualified names, prefixes and nested scopes.
    /// </summary>
    public static IEnumerable<object[]> RenderedTypeNames() {
        yield return new object[] { "Foo" };
        yield return new object[] { "::Foo" };
        yield return new object[] { "List<Foo>" };
        yield return new object[] { "Foo*" };
        yield return new object[] { "const Foo&" };
        yield return new object[] { "Foo_1<Bar>" };
        yield return new object[] { "NotFoo" };
        yield return new object[] { "FooBar" };
        yield return new object[] { "Foo::Nested" };
        yield return new object[] { "Dictionary<Foo, List<Bar>>" };
        yield return new object[] { "Outer_Foo" };
        yield return new object[] { "Foo Foo" };
        yield return new object[] { "std::vector<Foo>" };
        yield return new object[] { "" };
        yield return new object[] { "int32_t" };
    }

    /// <summary>
    /// The qualifier inserts the same scope prefixes as the regex oracle for every rendered type string, when every
    /// emitted name in the index is a pure identifier.
    /// </summary>
    [Theory]
    [MemberData(nameof(RenderedTypeNames))]
    public void Qualify_MatchesRegexOracle(string renderedTypeName) {
        string[] emittedTypeNames = { "Foo", "Bar", "Foo_1", "Nested", "List" };
        CPPEmittedTypeNameIndex index = BuildIdentifierOnlyIndex(emittedTypeNames);
        Assert.True(index.AllEmittedTypeNamesAreIdentifiers);

        string expected = ReferenceQualify(renderedTypeName, emittedTypeNames);
        string actual = CPPGeneratedTypeNameQualifier.Qualify(renderedTypeName, index);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// An empty name set returns the input instance untouched.
    /// </summary>
    [Fact]
    public void Qualify_WithNoEmittedNames_ReturnsSameInstance() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        CPPEmittedTypeNameIndex index = CPPEmittedTypeNameIndex.Build(program.Classes);

        string rendered = "List<Foo>";
        Assert.Same(rendered, CPPGeneratedTypeNameQualifier.Qualify(rendered, index));
    }

    /// <summary>
    /// A null index is a programming error, not a case to paper over with a default: the qualifier throws.
    /// </summary>
    [Fact]
    public void Qualify_WithNullIndex_Throws() {
        Assert.Throws<ArgumentNullException>(() => CPPGeneratedTypeNameQualifier.Qualify("List<Foo>", null));
    }

    /// <summary>
    /// When the only emitted name is a non-identifier string ("Foo Foo", with no co-occurring bare "Foo" alias),
    /// the qualifier must still match the regex oracle: "Foo Foo" gets qualified as a whole, and "Foo" alone stays untouched.
    /// </summary>
    [Theory]
    [InlineData("Foo Foo", "::Foo Foo")]
    [InlineData("Foo", "Foo")]
    [InlineData("prefix Foo Foo suffix", "prefix ::Foo Foo suffix")]
    public void Qualify_WithNonIdentifierEmittedNameAndNoBareAlias_MatchesRegexOracle(string renderedTypeName, string expectedFromOracleShape) {
        string[] emittedTypeNames = { "Foo Foo", "Bar" };
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        CreateGeneratedClass(program, "FooFooClass", codeGenRename: "Foo Foo");
        CreateGeneratedClass(program, "BarClass", codeGenRename: "Bar");
        CPPEmittedTypeNameIndex index = CPPEmittedTypeNameIndex.Build(program.Classes);

        string expected = ReferenceQualify(renderedTypeName, emittedTypeNames);
        string actual = CPPGeneratedTypeNameQualifier.Qualify(renderedTypeName, index);

        Assert.Equal(expected, actual);
        Assert.Equal(expectedFromOracleShape, actual);
    }

    /// <summary>
    /// <see cref="CPPEmittedTypeNameIndex.AllEmittedTypeNamesAreIdentifiers"/> is true for identifier-only programs
    /// and false once a <c>CodeGenRename</c> introduces a non-identifier emitted name such as "Foo Foo".
    /// </summary>
    [Fact]
    public void AllEmittedTypeNamesAreIdentifiers_ReflectsWhetherEveryEmittedNameIsAPureIdentifier() {
        CPPEmittedTypeNameIndex identifierOnlyIndex = BuildIdentifierOnlyIndex("Foo", "Bar", "Foo_1");
        Assert.True(identifierOnlyIndex.AllEmittedTypeNamesAreIdentifiers);

        CPPProgram programWithNonIdentifierName = new CPPProgram(new CPPConversionRules());
        CreateGeneratedClass(programWithNonIdentifierName, "Foo");
        CreateGeneratedClass(programWithNonIdentifierName, "FooFooClass", codeGenRename: "Foo Foo");
        CPPEmittedTypeNameIndex nonIdentifierIndex = CPPEmittedTypeNameIndex.Build(programWithNonIdentifierName.Classes);

        Assert.False(nonIdentifierIndex.AllEmittedTypeNamesAreIdentifiers);
    }
}
