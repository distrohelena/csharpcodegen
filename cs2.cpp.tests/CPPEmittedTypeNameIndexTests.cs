using cs2.core;
using cs2.cpp;
using cs2.cpp.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the per-emit-pass emitted type name index used to replace linear class scans.
/// </summary>
public sealed class CPPEmittedTypeNameIndexTests {
    /// <summary>
    /// Creates a generated class registered on a fresh program.
    /// </summary>
    static ConversionClass CreateGeneratedClass(CPPProgram program, string name, bool isNative = false) {
        ConversionClass conversionClass = new ConversionClass {
            Name = name,
            IsNative = isNative,
            Program = program
        };
        program.Classes.Add(conversionClass);
        return conversionClass;
    }

    /// <summary>
    /// Every class in the program gets an indexed name; generated-class lookups skip native classes that share the name.
    /// </summary>
    [Fact]
    public void Build_IndexesEveryClassAndResolvesGeneratedClassesOnly() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        ConversionClass nativeList = CreateGeneratedClass(program, "List", isNative: true);
        ConversionClass nativeWidget = CreateGeneratedClass(program, "Widget", isNative: true);
        ConversionClass widget = CreateGeneratedClass(program, "Widget");
        ConversionClass gadget = CreateGeneratedClass(program, "Gadget");

        CPPEmittedTypeNameIndex index = CPPEmittedTypeNameIndex.Build(program.Classes);

        Assert.True(index.TryGetEmittedTypeName(widget, out string widgetName));
        Assert.Equal("Widget", widgetName);
        Assert.True(index.TryGetEmittedTypeName(nativeList, out string nativeName));
        Assert.Equal("List", nativeName);
        Assert.True(index.TryGetGeneratedClass("Widget", out ConversionClass resolvedWidget));
        Assert.Same(widget, resolvedWidget);
        Assert.NotSame(nativeWidget, resolvedWidget);
        Assert.True(index.TryGetGeneratedClass("Gadget", out ConversionClass resolvedGadget));
        Assert.Same(gadget, resolvedGadget);
        Assert.False(index.TryGetGeneratedClass("List", out _));
        Assert.Contains("Widget", index.EmittedTypeNames);
        Assert.Contains("List", index.EmittedTypeNames);
        Assert.Contains("Gadget", index.EmittedTypeNames);
        Assert.False(index.TryGetGeneratedClass("Missing", out _));
    }

    /// <summary>
    /// The program exposes no index until an emit pass builds it, and clearing removes it again.
    /// </summary>
    [Fact]
    public void Program_BuildsAndClearsIndex() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        ConversionClass widget = CreateGeneratedClass(program, "Widget");

        Assert.Null(program.EmittedTypeNameIndex);
        program.BuildEmittedTypeNameIndex();
        Assert.NotNull(program.EmittedTypeNameIndex);
        Assert.Equal("Widget", widget.GetEmittedTypeName());
        program.ClearEmittedTypeNameIndex();
        Assert.Null(program.EmittedTypeNameIndex);
        Assert.Equal("Widget", widget.GetEmittedTypeName());
    }

    /// <summary>
    /// Building the index never asserts a collision against a class the real emission path would never emit.
    /// </summary>
    [Fact]
    public void Build_DoesNotAssertCollisionsForNonEmittableClasses() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        CSharpCompilation compilation = RoslynTestHelper.CreateCompilation(
            "namespace cs2.attributes { public class NativeOwnedMemberAttribute : System.Attribute { } }");
        INamedTypeSymbol excludedSymbol = compilation.GetTypeByMetadataName("cs2.attributes.NativeOwnedMemberAttribute");

        ConversionClass excludedClass = new ConversionClass {
            Name = "NativeOwnedMemberAttribute",
            IsNative = false,
            Program = program,
            TypeSymbol = excludedSymbol
        };
        program.Classes.Add(excludedClass);
        ConversionClass plainClass = CreateGeneratedClass(program, "NativeOwnedMemberAttribute");

        CPPEmittedTypeNameIndex index = CPPEmittedTypeNameIndex.Build(program.Classes);

        Assert.True(index.TryGetGeneratedClass("NativeOwnedMemberAttribute", out ConversionClass resolvedClass));
        Assert.Same(plainClass, resolvedClass);
        Assert.True(index.TryGetEmittedTypeName(excludedClass, out string excludedName));
        Assert.Equal("NativeOwnedMemberAttribute", excludedName);
        Assert.True(index.TryGetEmittedTypeName(plainClass, out string plainName));
        Assert.Equal("NativeOwnedMemberAttribute", plainName);
    }

    /// <summary>
    /// A class renamed after the index was built keeps returning the indexed name, proving lookups no longer recompute.
    /// </summary>
    [Fact]
    public void GetEmittedTypeName_UsesIndexWhenPresent() {
        CPPProgram program = new CPPProgram(new CPPConversionRules());
        ConversionClass widget = CreateGeneratedClass(program, "Widget");
        program.BuildEmittedTypeNameIndex();

        widget.Name = "Renamed";

        Assert.Equal("Widget", widget.GetEmittedTypeName());
        program.ClearEmittedTypeNameIndex();
        Assert.Equal("Renamed", widget.GetEmittedTypeName());
    }
}
