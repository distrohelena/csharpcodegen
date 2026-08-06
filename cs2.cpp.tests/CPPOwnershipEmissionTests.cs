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
    /// Ensures returning a successful type-pattern alias transfers the allocation owned by the pattern input local.
    /// </summary>
    [Fact]
    public void WriteOutput_WithReturnedTypePatternAlias_DisarmsSourceOwnerBeforeReturn() {
        string outputPath = Convert("""
            public class Asset {
            }

            public sealed class SceneAsset : Asset {
            }

            public static class Serializer {
                public static Asset Deserialize() {
                    return new SceneAsset();
                }
            }

            public sealed class Processor<TAsset> where TAsset : Asset {
                public TAsset Read() {
                    Asset asset = Serializer.Deserialize();
                    if (asset is TAsset typedAsset) {
                        return typedAsset;
                    }

                    throw new System.InvalidOperationException();
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Processor_1.cpp"));

        AssertAppearsInOrder(sourceOutput, "bool __owns_asset_", " = false;", "return typedAsset;");
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
    /// Ensures inserting an owned local into a collection disarms caller cleanup so the stored entry cannot dangle.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedLocalAddedToCollection_DisarmsBeforeInsertion() {
        string outputPath = Convert("""
            using System.Collections.Generic;

            public sealed class Consumer {
                readonly List<List<int>> Records = new List<List<int>>();

                public void Run() {
                    List<int> record = new List<int>();
                    Records.Add(record);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(sourceOutput, "bool __owns_record_", " = false;", "->Add(record)");
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
    /// Ensures array-wide native release destroys every owned item, deletes the container, clears the pointer, and disarms caller cleanup.
    /// </summary>
    [Fact]
    public void WriteOutput_WithDeleteItemsAndRelease_LowersCompleteArrayCleanupInline() {
        string outputPath = Convert("""
            public static class NativeOwnership {
                public static void DeleteItemsAndRelease<T>(ref T[] values) where T : class {
                }
            }

            public sealed class Item {
            }

            public sealed class Consumer {
                public void Run() {
                    Item[] values = new Item[] { new Item(), new Item() };
                    NativeOwnership.DeleteItemsAndRelease(ref values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        Assert.Contains("delete (*__nativeOwnershipTarget_", sourceOutput, StringComparison.Ordinal);
        AssertAppearsInOrder(
            sourceOutput,
            "auto __nativeOwnershipTarget_",
            " = values;",
            "delete (*__nativeOwnershipTarget_",
            "delete __nativeOwnershipTarget_",
            "values = nullptr;",
            "__owns_values_",
            " = false;");
        Assert.DoesNotContain("NativeOwnership::DeleteItemsAndRelease", sourceOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures array-wide disposable release invokes disposal before deleting each item and then clears the owned container.
    /// </summary>
    [Fact]
    public void WriteOutput_WithDisposeItemsAndRelease_LowersCompleteArrayCleanupInline() {
        string outputPath = Convert("""
            using System;

            public static class NativeOwnership {
                public static void DisposeItemsAndRelease<T>(ref T[] values) where T : class, IDisposable {
                }
            }

            public sealed class Item : IDisposable {
                public void Dispose() {
                }
            }

            public sealed class Consumer {
                public void Run() {
                    Item[] values = new Item[] { new Item(), new Item() };
                    NativeOwnership.DisposeItemsAndRelease(ref values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(
            sourceOutput,
            "auto __nativeOwnershipTarget_",
            " = values;",
            ")[index]->Dispose();",
            "delete (*__nativeOwnershipTarget_",
            "delete __nativeOwnershipTarget_",
            "values = nullptr;");
        Assert.DoesNotContain("NativeOwnership::DisposeItemsAndRelease", sourceOutput, StringComparison.Ordinal);
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
    /// Ensures assigning an owned concrete local through an owned interface property transfers cleanup after the property setter succeeds.
    /// </summary>
    [Fact]
    public void WriteOutput_WithPolymorphicOwnedPropertyTransfer_DisarmsAfterAssignment() {
        string outputPath = Convert("""
            using System;

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class NativeOwnedMemberAttribute : Attribute {
            }

            public static class NativeOwnership {
                public static void Delete<T>(T value) where T : class {
                }
            }

            public interface IResource {
            }

            public sealed class Resource : IResource {
            }

            public sealed class Holder : IDisposable {
                [NativeOwnedMember]
                public IResource Value { get; private set; }

                public static Holder Build() {
                    Holder holder = new Holder();
                    Resource resource = new Resource();
                    holder.Value = resource;
                    return holder;
                }

                public void Dispose() {
                    NativeOwnership.Delete(Value);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Holder.cpp"));

        AssertAppearsInOrder(sourceOutput, "holder->set_Value(resource);", "__owns_resource_", " = false;");
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
    /// Ensures an owned using-statement resource is disposed and deleted exactly once before its ownership guard exits.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedUsingResource_EmitsSingleDeleteAcrossCleanupGuards() {
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
        AssertAppearsInOrder(sourceOutput, "__localDeleteGuard", "delete resource;", "__usingDisposeGuard", "resource->Dispose();");
        Assert.Equal(1, sourceOutput.Split("delete resource;", StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// Ensures an owned using declaration retains disposal and exactly-once native deletion through the remainder scope.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedUsingDeclaration_EmitsSingleDeleteAcrossCleanupGuards() {
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
        AssertAppearsInOrder(sourceOutput, "__localDeleteGuard", "delete resource;", "__usingDisposeGuard", "resource->Dispose();");
        Assert.Equal(1, sourceOutput.Split("delete resource;", StringSplitOptions.None).Length - 1);
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
    /// Ensures an object initializer transfers a tracked local into an owned member before the initializer lambda returns.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedLocalObjectInitializerMember_TransfersLocalCleanupToObject() {
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
                public List<int> Values;

                public void Dispose() {
                    NativeOwnership.Delete(Values);
                }
            }

            public sealed class Consumer {
                public Holder Build() {
                    List<int> values = new List<int>();
                    return new Holder {
                        Values = values
                    };
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Consumer.cpp"));

        AssertAppearsInOrder(
            sourceOutput,
            "__object_",
            "->Values = values;",
            "__owns_values_",
            " = false;",
            "return __object_");
    }

    /// <summary>
    /// Ensures assigning an owned local to an out parameter transfers cleanup responsibility to the caller.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedLocalOutParameterAssignment_DisarmsAfterAssignment() {
        string outputPath = Convert("""
            public static class Factory {
                public static bool TryBuild(out byte[] data) {
                    byte[] localData = new byte[] { 1, 2, 3, 4 };
                    data = localData;
                    return true;
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Factory.cpp"));

        AssertAppearsInOrder(sourceOutput, "data = localData", "__owns_localData_", " = false;");
    }

    /// <summary>
    /// Ensures storing an owned local in an array element transfers cleanup responsibility to the containing storage.
    /// </summary>
    [Fact]
    public void WriteOutput_WithOwnedLocalArrayElementAssignment_DisarmsAfterAssignment() {
        string outputPath = Convert("""
            public static class NativeOwnership {
                public static void DeleteItemsAndRelease<T>(ref T[] values) where T : class {
                }
            }

            public sealed class Owner : System.IDisposable {
                byte[][] Values = new byte[1][];

                public void Store() {
                    byte[] data = new byte[] { 1, 2, 3, 4 };
                    Values[0] = data;
                }

                public void Dispose() {
                    NativeOwnership.DeleteItemsAndRelease(ref Values);
                }
            }
            """);
        string sourceOutput = File.ReadAllText(Path.Combine(outputPath, "Owner.cpp"));

        AssertAppearsInOrder(sourceOutput, "(*this->Values)[0] = data", "__owns_data_", " = false;");
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
