using cs2.cpp;

#nullable disable

namespace cs2.cpp.tests;

/// <summary>
/// Verifies generated C++ lifetime operations are emitted from semantic native ownership plans.
/// </summary>
public sealed class CPPOwnershipEmissionTests {
    /// <summary>
    /// Ensures an owned factory result receives caller cleanup while the factory disarms cleanup before returning its local.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedFactoryLocal_EmitsCallerGuardAndFactoryTransfer() {
        string outputPath = Convert("""
            using System.Collections.Generic;

            public static class Factory {
                public static List<int> Build() {
                    List<int> values = new List<int>();
                    return values;
                }
            }

            public sealed class Consumer {
                public int Run() {
                    List<int> values = Factory.Build();
                    return values.Count;
                }
            }
            """);
        string factoryOutput = File.ReadAllText(Path.Combine(outputPath, "Factory.cpp"));
        string consumerOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        Assert.Contains("List<int32_t> *values = Factory::Build();", consumerOutput, StringComparison.Ordinal);
        Assert.Contains("bool __owns_values_", consumerOutput, StringComparison.Ordinal);
        Assert.Contains("he_cpp_make_scope_exit", consumerOutput, StringComparison.Ordinal);
        Assert.Contains("if (__owns_values_", consumerOutput, StringComparison.Ordinal);
        Assert.Contains("delete values;", consumerOutput, StringComparison.Ordinal);
        AssertAppearsInOrder(factoryOutput, "bool __owns_values_", " = false;", "return values;");
    }

    /// <summary>
    /// Ensures passing an owned local to a takes-ownership parameter disarms caller cleanup before the invocation.
    /// </summary>
    [Fact]
    public void WriteOutput_WithTakesOwnershipArgument_DisarmsBeforeInvocation() {
        string outputPath = Convert("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public sealed class Sink {
                public void Take([NativeTakesOwnership] List<int> values) {
                    NativeOwnership.Delete(values);
                }
            }

            public sealed class Consumer {
                public void Run(Sink sink) {
                    List<int> values = new List<int>();
                    sink.Take(values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "bool __owns_values_", " = false;", "sink->Take(values)");
    }

    /// <summary>
    /// Ensures explicit native deletion disarms the local guard immediately after destroying the owned value.
    /// </summary>
    [Fact]
    public void WriteOutput_WithExplicitDelete_DeletesThenDisarmsGuard() {
        string outputPath = Convert("""
            using System.Collections.Generic;

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public sealed class Consumer {
                public void Run() {
                    List<int> values = new List<int>();
                    NativeOwnership.Delete(values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));
        int explicitDeleteIndex = sourceOutput.LastIndexOf("delete values;", StringComparison.Ordinal);
        int disarmIndex = sourceOutput.IndexOf(" = false;", explicitDeleteIndex, StringComparison.Ordinal);

        Assert.True(explicitDeleteIndex >= 0);
        Assert.True(disarmIndex > explicitDeleteIndex);
    }

    /// <summary>
    /// Ensures explicit native release clears the local pointer and then disarms its conditional guard.
    /// </summary>
    [Fact]
    public void WriteOutput_WithExplicitRelease_ClearsThenDisarmsGuard() {
        string outputPath = Convert("""
            using System.Collections.Generic;

            public static class NativeOwnership {
                public static void Release<T>(ref T value) where T : class {
                    value = null;
                }
            }

            public sealed class Consumer {
                public void Run() {
                    List<int> values = new List<int>();
                    NativeOwnership.Release(ref values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "delete values;", "values = nullptr;", "__owns_values_", " = false;");
    }

    /// <summary>
    /// Ensures assigning an owned local into a native-owned member disarms local cleanup only after assignment succeeds.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedMemberTransfer_DisarmsAfterAssignment() {
        string outputPath = Convert("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public sealed class Holder : IDisposable {
                [NativeOwnedMember]
                List<int> Values;

                public Holder() {
                    Values = new List<int>();
                }

                public void Replace() {
                    NativeOwnership.Delete(Values);
                    List<int> values = new List<int>();
                    Values = values;
                }

                public void Dispose() {
                    NativeOwnership.Delete(Values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Holder.cpp"));

        AssertAppearsInOrder(sourceOutput, "this->Values = values", "__owns_values_", " = false;");
    }

    /// <summary>
    /// Ensures passing an owned local to a takes-ownership constructor disarms caller cleanup before construction.
    /// </summary>
    [Fact]
    public void WriteOutput_WithTakesOwnershipConstructor_DisarmsBeforeCreation() {
        string outputPath = Convert("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public sealed class Asset : IDisposable {
                [NativeOwnedMember]
                List<int> Values;

                public Asset([NativeTakesOwnership] List<int> values) {
                    Values = values;
                }

                public void Dispose() {
                    NativeOwnership.Delete(Values);
                }
            }

            public sealed class Consumer {
                public Asset Build() {
                    List<int> values = new List<int>();
                    return new Asset(values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "bool __owns_values_", "auto __ctor_arg", " = values;", "auto __constructedValue", "new ::Asset(__ctor_arg", " = false;", "return __constructedValue");
    }

    /// <summary>
    /// Ensures a borrowed local remains an ordinary pointer alias without an ownership flag or delete guard.
    /// </summary>
    [Fact]
    public void WriteOutput_WithBorrowedLocal_EmitsNoOwnershipState() {
        string outputPath = Convert("""
            using System.Collections.Generic;

            public sealed class Consumer {
                public int Run(List<int> shared) {
                    List<int> values = shared;
                    return values.Count;
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        Assert.Contains("List<int32_t> *values = shared;", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("__owns_values_", sourceOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("delete values;", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures each owned declarator receives independent state and replacement evaluates before old-value deletion.
    /// </summary>
    [Fact]
    public void WriteOutput_WithMultipleOwnedLocalsAndReplacement_EmitsIndependentOrderedCleanup() {
        string outputPath = Convert("""
            using System.Collections.Generic;

            public sealed class Consumer {
                public void Run() {
                    List<int> first = new List<int>(), second = new List<int>();
                    first = new List<int>();
                    second = null;
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        Assert.Contains("bool __owns_first_", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("bool __owns_second_", sourceOutput, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(sourceOutput, "auto __localDeleteGuard"));
        Assert.Equal(2, CountOccurrences(sourceOutput, "if (__owns_first_"));
        Assert.Equal(2, CountOccurrences(sourceOutput, "if (__owns_second_"));
        AssertAppearsInOrder(sourceOutput, "auto __reassignValue", "delete first;", "first = __reassignValue");
        AssertAppearsInOrder(sourceOutput, "delete second;", "second = nullptr", "__owns_second_");
    }

    /// <summary>
    /// Ensures an uninitialized local that later acquires owned storage receives a false-initialized guard before assignment.
    /// </summary>
    [Fact]
    public void WriteOutput_WithDelayedOwnedInitialization_EmitsGuardAndAcquisition() {
        string outputPath = Convert("""
            using System.Collections.Generic;

            public sealed class Consumer {
                public void Run() {
                    List<int> values;
                    values = new List<int>();
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "List<int32_t> *values", "bool __owns_values_", " = false;", "new List<int32_t>()", "__owns_values_", " = true");
        Assert.Contains("delete values;", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures an owned for-initializer local is guarded by an enclosing native scope for the complete loop lifetime.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedForInitializer_EmitsGuardOutsideLoop() {
        string outputPath = Convert("""
            using System.Collections.Generic;

            public sealed class Consumer {
                public void Run() {
                    for (List<int> values = new List<int>(); values.Count < 1; values.Add(1)) {
                    }
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "List<int32_t> *values", "bool __owns_values_", "he_cpp_make_scope_exit", "for (;", "values->get_Count()");
        Assert.Contains("delete values;", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures an owned using-statement resource is disposed before its native storage guard deletes it.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedUsingResource_EmitsDisposalAndDeleteGuards() {
        string outputPath = Convert("""
            using System;

            public sealed class Resource : IDisposable {
                public void Dispose() {
                }
            }

            public sealed class Consumer {
                public void Run() {
                    using (Resource resource = new Resource()) {
                    }
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "Resource *resource", "bool __owns_resource_", "__localDeleteGuard", "__usingDisposeGuard");
        Assert.Contains("delete resource;", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("resource->Dispose();", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures an owned using declaration retains both disposal and native deletion through the remainder scope.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedUsingDeclaration_EmitsDisposalAndDeleteGuards() {
        string outputPath = Convert("""
            using System;

            public sealed class Resource : IDisposable {
                public void Dispose() {
                }
            }

            public sealed class Consumer {
                public void Run() {
                    using Resource resource = new Resource();
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "Resource *resource", "bool __owns_resource_", "__localDeleteGuard", "__usingDisposeGuard");
        Assert.Contains("delete resource;", sourceOutput, StringComparison.Ordinal);
        Assert.Contains("resource->Dispose();", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures dependent declarators install the first local's guard before a later initializer transfers that local.
    /// </summary>
    [Fact]
    public void WriteOutput_WithDependentOwnedDeclarators_GuardsBeforeLaterTransfer() {
        string outputPath = Convert("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public static class Factory {
                public static List<int> Replace([NativeTakesOwnership] List<int> prior) {
                    NativeOwnership.Delete(prior);
                    return new List<int>();
                }
            }

            public sealed class Consumer {
                public void Run() {
                    List<int> first = new List<int>(), second = Factory.Replace(first);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "List<int32_t> *first", "bool __owns_first_", " = false;", "List<int32_t> *second", "Factory::Replace(first)", "bool __owns_second_");
    }

    /// <summary>
    /// Ensures ownership transfer through a reduced extension receiver disarms caller cleanup before the static extension call.
    /// </summary>
    [Fact]
    public void WriteOutput_WithTakesOwnershipReducedExtension_DisarmsBeforeCall() {
        string outputPath = Convert("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public static class Extensions {
                public static void Consume([NativeTakesOwnership] this List<int> values) {
                    NativeOwnership.Delete(values);
                }
            }

            public sealed class Consumer {
                public void Run() {
                    List<int> values = new List<int>();
                    values.Consume();
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "bool __owns_values_", " = false;", "Extensions::Consume(values)");
    }

    /// <summary>
    /// Ensures constructor argument preparation finishes before ownership is transferred into the constructor call.
    /// </summary>
    [Fact]
    public void WriteOutput_WithTakesOwnershipConstructorAndLaterArgument_DisarmsAfterArgumentPreparation() {
        string outputPath = Convert("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeTakesOwnershipAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public sealed class Asset : IDisposable {
                [NativeOwnedMember]
                List<int> Values;

                public Asset([NativeTakesOwnership] List<int> values, int marker) {
                    Values = values;
                }

                public void Dispose() {
                    NativeOwnership.Delete(Values);
                }
            }

            public static class Factory {
                public static int BuildMarker() {
                    return 1;
                }
            }

            public sealed class Consumer {
                public Asset Build() {
                    List<int> values = new List<int>();
                    return new Asset(values, Factory.BuildMarker());
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "Factory::BuildMarker()", "auto __constructedValue", "new ::Asset(", "__owns_values_", " = false;", "return __constructedValue");
    }

    /// <summary>
    /// Ensures a nested owned-local replacement lowers as a value-producing lambda with ordered cleanup.
    /// </summary>
    [Fact]
    public void WriteOutput_WithNestedOwnedReplacement_EmitsValidValueExpression() {
        string outputPath = Convert("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeNoEscapeAttribute : Attribute {
            }

            public sealed class Consumer {
                public void Run() {
                    List<int> values = new List<int>();
                    Use(values = new List<int>());
                }

                static void Use([NativeNoEscape] List<int> values) {
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "auto __reassignValue", "delete values;", "values = __reassignValue", " = true;", "return values;", "})()");
        Assert.Contains("Consumer::Use((", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures a nested owned-member transfer returns the assigned value only after disarming caller cleanup.
    /// </summary>
    [Fact]
    public void WriteOutput_WithNestedOwnedMemberTransfer_EmitsValidValueExpression() {
        string outputPath = Convert("""
            using System;
            using System.Collections.Generic;

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class NativeNoEscapeAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public sealed class Holder : IDisposable {
                [NativeOwnedMember]
                public List<int> Values;

                public Holder() {
                    Values = new List<int>();
                }

                public void Dispose() {
                    NativeOwnership.Delete(Values);
                }
            }

            public sealed class Consumer {
                public void Run(Holder holder) {
                    NativeOwnership.Delete(holder.Values);
                    List<int> values = new List<int>();
                    Use(GetHolder(holder).Values = values);
                }

                static void Use([NativeNoEscape] List<int> values) {
                }

                static Holder GetHolder(Holder holder) {
                    return holder;
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "auto&& __ownershipTarget", "Consumer::GetHolder(holder)", "auto __ownershipAssignmentValue", "__ownershipTarget", "->Values = values", "__owns_values_", " = false;", "return __ownershipAssignmentValue", "})()");
        Assert.Contains("Consumer::Use((", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts one source fixture into a workspace-owned generated C++ directory.
    /// </summary>
    /// <param name="source">Complete C# source converted by the production pipeline.</param>
    /// <returns>The generated C++ output directory.</returns>
    static string Convert(string source) {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "ownership-emission", Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(fixtureRoot, "Fixture.csproj");
        string outputPath = Path.Combine(fixtureRoot, "generated");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(projectPath, CreateProjectFile());
        File.WriteAllText(Path.Combine(fixtureRoot, "Fixture.cs"), source);

        CPPConversionOptions options = CPPConversionOptions.CreateDefault();
        options.LoadNativeRuntimeMetadata = false;
        CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
        converter.AddCsproj(projectPath);
        converter.WriteOutput(outputPath);
        return outputPath;
    }

    /// <summary>
    /// Creates minimal SDK project text for an ownership emission fixture.
    /// </summary>
    /// <returns>Complete SDK project XML.</returns>
    static string CreateProjectFile() {
        return """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
            </Project>
            """;
    }

    /// <summary>
    /// Asserts that each expected fragment appears after the preceding fragment in generated output.
    /// </summary>
    /// <param name="text">Generated source text to inspect.</param>
    /// <param name="fragments">Ordered fragments expected in the source text.</param>
    static void AssertAppearsInOrder(string text, params string[] fragments) {
        int searchStart = 0;
        foreach (string fragment in fragments) {
            int fragmentIndex = text.IndexOf(fragment, searchStart, StringComparison.Ordinal);
            Assert.True(fragmentIndex >= searchStart, $"Expected fragment '{fragment}' after offset {searchStart}.\n{text}");
            searchStart = fragmentIndex + fragment.Length;
        }
    }

    /// <summary>
    /// Counts non-overlapping occurrences of one fragment in generated output.
    /// </summary>
    /// <param name="text">Generated source text to inspect.</param>
    /// <param name="fragment">Non-empty fragment whose occurrences are counted.</param>
    /// <returns>The number of non-overlapping fragment occurrences.</returns>
    static int CountOccurrences(string text, string fragment) {
        if (string.IsNullOrEmpty(fragment)) {
            throw new ArgumentException("A fragment is required.", nameof(fragment));
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
