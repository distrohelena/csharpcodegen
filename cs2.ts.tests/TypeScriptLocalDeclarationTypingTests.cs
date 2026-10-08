using System.Collections.Generic;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptLocalDeclarationTypingTests {
        [Fact]
        public void UninitializedExplicitLocalRetainsItsElementType() {
            const string source = "using System.Collections.Generic; class Packet { } class C { void M() { List<Packet> packets; } }";
            string output = EmitBlock(source);
            Assert.Contains("let packets: List<Packet>", output);
        }

        [Fact]
        public void ReadOnlyListInitializedWithArrayEmptyRetainsItsContract() {
            const string source = "using System; using System.Collections.Generic; class Packet { } class C { void M() { IReadOnlyList<Packet> packets = Array.Empty<Packet>(); } }";
            string output = EmitBlock(source);
            Assert.Contains("let packets: IReadOnlyList<Packet> = []", output);
        }

        [Fact]
        public void DelegateSnapshotOfFieldLikeEventUsesEventRuntimeType() {
            const string source = "using System; class C { event Action<int> committed; void M() { Action<int> handlers; handlers = committed; } }";
            string output = EmitBlock(source);
            Assert.Contains("let handlers: Event", output);
            Assert.Contains("handlers = this.committed", output);
        }

        static string EmitBlock(string source) {
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var method = RoslynTestHelper.GetFirstMethod(compilation.Root);
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context);
            var lines = new List<string>();
            harness.Processor.ProcessBlock(compilation.Model, harness.Context, method.Body!, lines, depth: 0);
            return TsProcessorTestHarness.JoinLines(lines);
        }
    }
}
