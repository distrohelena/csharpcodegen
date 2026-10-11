using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>
/// Executes event subscriptions through native interfaces against the concrete publisher storage.
/// </summary>
public sealed class CPPInterfaceEventCompilationTests {
    /// <summary>
    /// Verifies subscription and removal through inherited and independent interfaces share concrete event storage.
    /// </summary>
    [Fact]
    public void InterfaceEvents_NotifyConcreteAndInheritedPublishers() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(InterfaceEvents_NotifyConcreteAndInheritedPublishers),
            """
            using System;
            public interface IChanged { event Action Changed; }
            public interface IAlsoChanged { event Action Changed; }
            public interface IChildChanged : IChanged { }
            public class Publisher {
                public event Action Changed;
                public void Raise() { Changed?.Invoke(); }
            }
            public sealed class InheritedPublisher : Publisher, IChildChanged, IAlsoChanged { }
            public sealed class DirectPublisher : IChanged, IAlsoChanged {
                public event Action Changed;
                public void Raise() { Changed?.Invoke(); }
            }
            public class ContractPublisher : IChanged {
                public event Action Changed;
                public void RaiseOriginal() { Changed?.Invoke(); }
            }
            public sealed class SplitPublisher : ContractPublisher, IAlsoChanged {
                public new event Action Changed;
                public void RaiseOther() { Changed?.Invoke(); }
            }
            public interface IValueChanged<T> { event Action<T> ValueChanged; }
            public sealed class ValuePublisher : IValueChanged<int> {
                public event Action<int> ValueChanged;
                public void Raise(int value) { ValueChanged?.Invoke(value); }
            }
            public sealed class Observer {
                public int Calls;
                public int Value;
                public void NotifyValue(int value) { Value = value; }
                public void SubscribeValue(IValueChanged<int> source) { source.ValueChanged += NotifyValue; }
                public void UnsubscribeValue(IValueChanged<int> source) { source.ValueChanged -= NotifyValue; }
                public void Notify() { Calls++; }
                public void Subscribe(IChanged source) { source.Changed += Notify; }
                public void Unsubscribe(IChanged source) { source.Changed -= Notify; }
                public void SubscribeOther(IAlsoChanged source) { source.Changed += Notify; }
                public void UnsubscribeOther(IAlsoChanged source) { source.Changed -= Notify; }
            }
            """);
        Assert.DoesNotContain("::Event Changed;", File.ReadAllText(Path.Combine(output.OutputPath, "IChanged.hpp")));
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            Observer first;
            Observer second;
            DirectPublisher direct;
            first.Subscribe(&direct);
            second.SubscribeOther(&direct);
            direct.Raise();
            if (first.Calls != 1 || second.Calls != 1) return 1;
            first.Unsubscribe(&direct);
            direct.Raise();
            if (first.Calls != 1 || second.Calls != 2) return 2;
            second.UnsubscribeOther(&direct);
            InheritedPublisher inherited;
            first.Subscribe(&inherited);
            second.SubscribeOther(&inherited);
            inherited.Raise();
            if (first.Calls != 2 || second.Calls != 3) return 3;
            first.Unsubscribe(&inherited);
            second.UnsubscribeOther(&inherited);
            inherited.Raise();
            if (first.Calls != 2 || second.Calls != 3) return 4;
            SplitPublisher split;
            first.Subscribe(&split);
            second.SubscribeOther(&split);
            split.RaiseOriginal();
            if (first.Calls != 3 || second.Calls != 3) return 5;
            split.RaiseOther();
            if (first.Calls != 3 || second.Calls != 4) return 6;
            first.Unsubscribe(&split);
            second.UnsubscribeOther(&split);
            split.RaiseOriginal();
            split.RaiseOther();
            if (first.Calls != 3 || second.Calls != 4) return 7;
            ValuePublisher values;
            first.SubscribeValue(&values);
            values.Raise(42);
            if (first.Value != 42) return 8;
            first.UnsubscribeValue(&values);
            values.Raise(99);
            if (first.Value != 42) return 9;
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, $"Native exit code: {result.ProgramExitCode}. {result.ProgramOutput}");
    }
}
