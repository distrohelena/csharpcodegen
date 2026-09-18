using cs2.core;
using cs2.cpp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies that the lazily cached generated class lookups refuse to rebuild while a parallel emission pass reads them.
/// </summary>
public sealed class ConversionProgramLookupFreezeTests {
    /// <summary>
    /// Creates a generated class registered on the supplied program.
    /// </summary>
    /// <param name="program">Program that receives the class.</param>
    /// <param name="name">Source type name of the class.</param>
    /// <returns>The registered generated class.</returns>
    static ConversionClass AddGeneratedClass(CPPProgram program, string name) {
        ConversionClass conversionClass = new ConversionClass {
            Name = name,
            IsNative = false,
            Program = program
        };
        program.Classes.Add(conversionClass);
        return conversionClass;
    }

    /// <summary>
    /// Returns the lookup key used by these fixtures.
    /// </summary>
    /// <param name="conversionClass">Class whose key is needed.</param>
    /// <returns>The class name used as the lookup key.</returns>
    static string SelectName(ConversionClass conversionClass) {
        return conversionClass.Name;
    }

    /// <summary>
    /// A frozen program rejects the rebuild triggered by a class added after the warm-up, and accepts it again after the thaw.
    /// </summary>
    [Fact]
    public void GetQualifiedGeneratedClassLookup_WhileFrozen_ThrowsInsteadOfRebuilding() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        AddGeneratedClass(program, "Alpha");

        Dictionary<string, ConversionClass> warmed = program.GetQualifiedGeneratedClassLookup(SelectName);
        Assert.True(warmed.ContainsKey("Alpha"));

        program.FreezeGeneratedClassLookups();
        Assert.Same(warmed, program.GetQualifiedGeneratedClassLookup(SelectName));

        AddGeneratedClass(program, "Beta");
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => program.GetQualifiedGeneratedClassLookup(SelectName));
        Assert.Equal(
            "Generated class lookups cannot be rebuilt while parallel emission is running; the class list changed after the lookups were warmed.",
            failure.Message);

        program.ThawGeneratedClassLookups();
        Dictionary<string, ConversionClass> rebuilt = program.GetQualifiedGeneratedClassLookup(SelectName);
        Assert.True(rebuilt.ContainsKey("Alpha"));
        Assert.True(rebuilt.ContainsKey("Beta"));
    }

    /// <summary>
    /// The name-and-arity lookup is frozen by the same flag and rebuilds once thawed.
    /// </summary>
    [Fact]
    public void GetGeneratedClassLookupByNameAndArity_WhileFrozen_ThrowsInsteadOfRebuilding() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        AddGeneratedClass(program, "Alpha");
        program.GetGeneratedClassLookupByNameAndArity(SelectName);

        program.FreezeGeneratedClassLookups();
        AddGeneratedClass(program, "Beta");
        Assert.Throws<InvalidOperationException>(() => program.GetGeneratedClassLookupByNameAndArity(SelectName));

        program.ThawGeneratedClassLookups();
        Assert.True(program.GetGeneratedClassLookupByNameAndArity(SelectName).ContainsKey("Beta"));
    }

    /// <summary>
    /// The base emitted name collision set is frozen by the same flag and rebuilds once thawed.
    /// </summary>
    [Fact]
    public void GetBaseEmittedTypeNameCollisions_WhileFrozen_ThrowsInsteadOfRebuilding() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        AddGeneratedClass(program, "Alpha");
        program.GetBaseEmittedTypeNameCollisions(SelectName);

        program.FreezeGeneratedClassLookups();
        AddGeneratedClass(program, "Alpha");
        Assert.Throws<InvalidOperationException>(() => program.GetBaseEmittedTypeNameCollisions(SelectName));

        program.ThawGeneratedClassLookups();
        Assert.Contains("Alpha", program.GetBaseEmittedTypeNameCollisions(SelectName));
    }
}
