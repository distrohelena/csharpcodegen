using System.Text.RegularExpressions;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Proves the single-pass qualifier matches the per-class regex replacement it replaces.
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
    /// The qualifier inserts the same scope prefixes as the regex oracle for every rendered type string.
    /// </summary>
    [Theory]
    [MemberData(nameof(RenderedTypeNames))]
    public void Qualify_MatchesRegexOracle(string renderedTypeName) {
        string[] emittedTypeNames = { "Foo", "Bar", "Foo_1", "Nested", "List", "Foo Foo" };
        HashSet<string> emittedSet = new HashSet<string>(emittedTypeNames, StringComparer.Ordinal);

        string expected = ReferenceQualify(renderedTypeName, emittedTypeNames);
        string actual = CPPGeneratedTypeNameQualifier.Qualify(renderedTypeName, emittedSet);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// An empty name set returns the input instance untouched.
    /// </summary>
    [Fact]
    public void Qualify_WithNoEmittedNames_ReturnsSameInstance() {
        string rendered = "List<Foo>";
        Assert.Same(rendered, CPPGeneratedTypeNameQualifier.Qualify(rendered, new HashSet<string>(StringComparer.Ordinal)));
    }
}
