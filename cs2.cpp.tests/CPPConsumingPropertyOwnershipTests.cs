using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Verifies consuming property setters use the same caller ownership contract as consuming methods.</summary>
public sealed class CPPConsumingPropertyOwnershipTests {
    /// <summary>Transfers owned arrays into a setter while preserving borrowed reads and exactly one release of each replaced item.</summary>
    [Fact]
    public void Owned_local_assignment_transfers_cleanup_to_consuming_setter() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(Owned_local_assignment_transfers_cleanup_to_consuming_setter),
            """
            using System;
            using cs2.attributes;
            public static class NativeOwnership {
                public static void DeleteItemsAndRelease<T>(ref T[] values) where T : class { values = null; }
            }
            public sealed class Item { public int Value; }
            public sealed class Owner : IDisposable {
                [NativeOwnedMember] Item[] items;
                [NativeBorrowedReturn]
                public Item[] Items {
                    get => items ?? Array.Empty<Item>();
                    [param: NativeTakesOwnership]
                    set {
                        if (items == value) return;
                        if (items != Array.Empty<Item>()) {
                            NativeOwnership.DeleteItemsAndRelease(ref items);
                        }
                        items = value;
                    }
                }
                public void Dispose() {
                    if (items != Array.Empty<Item>()) {
                        NativeOwnership.DeleteItemsAndRelease(ref items);
                    }
                }
                public void Assign(int value) {
                    Item[] replacement = new Item[1];
                    replacement[0] = new Item { Value = value };
                    Items = replacement;
                }
                public void Adopt([NativeTakesOwnership] Item[] value) { Items = value; }
            }
            public sealed class Consumer {
                public void Assign(Owner owner, int value) {
                    Item[] replacement = new Item[1];
                    replacement[0] = new Item { Value = value };
                    owner.Items = replacement;
                }
                public int Read(Owner owner) {
                    Item[] borrowed = owner.Items;
                    return borrowed.Length == 0 ? -1 : borrowed[0].Value;
                }
                public void Forward(Owner owner) {
                    Item[] replacement = new Item[1];
                    replacement[0] = new Item { Value = 37 };
                    owner.Adopt(replacement);
                }
                public void Clear(Owner owner) {
                    Item[] absent = null;
                    owner.Items = absent;
                    owner.Items = null;
                }
            }
            """,
            new Dictionary<string, string> { ["codegen-module-lifetime-pins"] = "true" });

        string consumer = File.ReadAllText(Path.Combine(output.OutputPath, "Consumer.cpp"));
        Assert.Contains("bool __owns_replacement_", consumer);
        string ownerFlag = System.Text.RegularExpressions.Regex.Match(consumer,
            @"bool (?<flag>__owns_replacement_\w+) = true;").Groups["flag"].Value;
        Assert.NotEmpty(ownerFlag);
        Assert.True(consumer.IndexOf(ownerFlag + " = false;", StringComparison.Ordinal) >
            consumer.IndexOf("set_Items(replacement)", StringComparison.Ordinal));
        Assert.DoesNotContain("delete borrowed;", consumer);
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            if (livePins != 0) return 1;
            {
                Owner owner;
                Consumer consumer;
                if (livePins != 2 || consumer.Read(&owner) != -1) return 2;
                consumer.Assign(&owner, 17);
                if (livePins != 3 || consumer.Read(&owner) != 17) return 3;
                owner.set_Items(owner.get_Items());
                if (livePins != 3 || consumer.Read(&owner) != 17) return 4;
                consumer.Assign(&owner, 23);
                if (livePins != 3 || consumer.Read(&owner) != 23) return 5;
                owner.Assign(31);
                if (livePins != 3 || consumer.Read(&owner) != 31) return 9;
                owner.set_Items(nullptr);
                if (livePins != 2 || consumer.Read(&owner) != -1) return 10;
                auto* empty = Array<Item*>::Empty();
                if (empty != owner.get_Items() || empty->get_Length() != 0) return 11;
                owner.Dispose();
                if (livePins != 2 || consumer.Read(&owner) != -1) return 6;
                owner.Dispose();
                if (livePins != 2) return 7;
                if (Array<Item*>::Empty() != empty || empty->get_Length() != 0) return 12;
                // Raw native defense only: the C# contract rejects this borrowed assignment.
                owner.set_Items(empty);
                owner.Dispose();
                owner.Dispose();
                if (livePins != 2 || empty->get_Length() != 0) return 13;
                consumer.Forward(&owner);
                if (livePins != 3 || consumer.Read(&owner) != 37) return 14;
                consumer.Clear(&owner);
                if (livePins != 2 || consumer.Read(&owner) != -1) return 16;
                owner.Dispose();
                if (livePins != 2 || Array<Item*>::Empty() != empty) return 15;
            }
            if (livePins != 0) return 8;
            """, """
            #define HELENGINE_CODE_MODULE_ID "test"
            static int livePins;
            void HeCppRetainCodeModule(const char*) { ++livePins; }
            void HeCppReleaseCodeModule(const char*) { --livePins; }
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
    }

    /// <summary>Rejects an owned local escaping through a setter with no consuming contract.</summary>
    [Fact]
    public void Owned_local_assignment_without_consuming_setter_still_fails() {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new CPPOwnershipConversionTestWorkspace().Convert(
                nameof(Owned_local_assignment_without_consuming_setter_still_fails),
                """
                public class Owner { public object Value { get; set; } }
                public class Consumer {
                    public void Assign(Owner owner) {
                        object value = new object();
                        owner.Value = value;
                    }
                }
                """));
        Assert.Contains("CPPOWN002", exception.Message);
    }

    /// <summary>Rejects borrowed getter results and the shared empty-array sentinel at a consuming setter boundary.</summary>
    /// <param name="borrowedExpression">Known borrowed value that cannot transfer native ownership.</param>
    /// <param name="declaration">Optional local alias whose existing ownership state must remain borrowed.</param>
    [Theory]
    [InlineData("owner.Values", "")]
    [InlineData("Array.Empty<object>()", "")]
    [InlineData("alias", "object[] alias = owner.Values;")]
    [InlineData("alias", "object[] alias = Array.Empty<object>();")]
    public void Borrowed_value_cannot_transfer_to_consuming_setter(string borrowedExpression, string declaration) {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new CPPOwnershipConversionTestWorkspace().Convert(
                nameof(Borrowed_value_cannot_transfer_to_consuming_setter),
                $$"""
                using System;
                using cs2.attributes;
                public static class NativeOwnership {
                    public static void DeleteItemsAndRelease<T>(ref T[] values) where T : class { values = null; }
                }
                public sealed class Owner : IDisposable {
                    [NativeOwnedMember] object[] values;
                    [NativeBorrowedReturn]
                    public object[] Values {
                        get => values;
                        [param: NativeTakesOwnership]
                        set { NativeOwnership.DeleteItemsAndRelease(ref values); values = value; }
                    }
                    public void Dispose() { NativeOwnership.DeleteItemsAndRelease(ref values); }
                }
                public sealed class Consumer {
                    public void Assign(Owner owner) { {{declaration}} owner.Values = {{borrowedExpression}}; }
                }
                """));
        Assert.Contains("CPPOWN002", exception.Message);
    }
}
