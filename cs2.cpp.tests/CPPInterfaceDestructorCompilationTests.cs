using cs2.cpp.tests.TestHelpers;

namespace cs2.cpp.tests;

/// <summary>Verifies native interface ownership reaches derived destructors and allows strict collection templates to instantiate safely.</summary>
public sealed class CPPInterfaceDestructorCompilationTests {
    /// <summary>Deleting a concrete adapter through an inherited interface invokes its cleanup in a compiled native program.</summary>
    [Fact]
    public void InterfaceDestructionDispatchesToConcreteCleanup() {
        using CPPOwnershipConversionOutput output = new CPPOwnershipConversionTestWorkspace().Convert(
            nameof(InterfaceDestructionDispatchesToConcreteCleanup),
            """
            public interface IValue { int GetValue(); }
            public interface IChildValue : IValue { }
            public sealed class ValueConsumer {
                public int Read(IChildValue value) { return value.GetValue(); }
            }
            """);
        Assert.Contains("virtual ~IValue() = default;", File.ReadAllText(Path.Combine(output.OutputPath, "IValue.hpp")));
        CPPGeneratedProgramResult result = new CPPGeneratedProgramRunner(output.OutputPath).Run("""
            bool destroyed = false;
            struct Adapter final : IChildValue {
                bool* Destroyed;
                explicit Adapter(bool* destroyed) : Destroyed(destroyed) {}
                ~Adapter() override { *Destroyed = true; }
                int32_t GetValue() override { return 42; }
            };
            IValue* value = new Adapter(&destroyed);
            if (value->GetValue() != 42) return 1;
            delete value;
            if (!destroyed) return 2;
            """);
        Assert.True(result.CompilerExitCode == 0, result.CompilerOutput);
        Assert.True(result.ProgramExitCode == 0, result.ProgramOutput);
    }
}
