using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Verifies enum-backed generic finite state machine usage converts cleanly through the C++ backend.
    /// </summary>
    public sealed class CPPFiniteStateMachineAuditTests {
        /// <summary>
        /// Ensures one representative FSM source shape with enum state usage emits stable generic and enum output without pseudo-includes for the generic state parameter.
        /// </summary>
        [Fact]
        public void WriteOutput_WithEnumBackedFiniteStateMachine_EmitsGenericTypeWithoutGenericParameterPseudoInclude() {
            string source = """
                using System;
                using System.Collections.Generic;

                public sealed class FiniteStateDefinition<TState> where TState : struct {
                    public Action<TState> OnEnter { get; set; }
                    public Action<TState> OnExit { get; set; }
                }

                public readonly struct FiniteStateTransitionKey<TState> where TState : struct {
                    public TState FromState { get; }
                    public TState ToState { get; }

                    public FiniteStateTransitionKey(TState fromState, TState toState) {
                        FromState = fromState;
                        ToState = toState;
                    }
                }

                public sealed class FiniteStateMachine<TState> where TState : struct {
                    readonly Dictionary<TState, FiniteStateDefinition<TState>> states = new Dictionary<TState, FiniteStateDefinition<TState>>();
                    readonly Dictionary<FiniteStateTransitionKey<TState>, Func<bool>> guards = new Dictionary<FiniteStateTransitionKey<TState>, Func<bool>>();

                    public void RegisterState(TState state, FiniteStateDefinition<TState> definition) {
                        states.Add(state, definition);
                    }

                    public void RegisterTransition(TState fromState, TState toState, Func<bool> canTransition) {
                        guards[new FiniteStateTransitionKey<TState>(fromState, toState)] = canTransition;
                    }
                }

                public enum TestState {
                    Waiting,
                    Playing
                }

                public sealed class TestConsumer {
                    public FiniteStateMachine<TestState> Build() {
                        FiniteStateMachine<TestState> machine = new FiniteStateMachine<TestState>();
                        machine.RegisterState(TestState.Waiting, new FiniteStateDefinition<TestState>());
                        machine.RegisterState(TestState.Playing, new FiniteStateDefinition<TestState>());
                        machine.RegisterTransition(TestState.Waiting, TestState.Playing, () => true);
                        return machine;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string generatedText = output.GeneratedText;

            Assert.Contains("template <typename TState>", generatedText, StringComparison.Ordinal);
            Assert.Contains("class FiniteStateMachine_1", generatedText, StringComparison.Ordinal);
            Assert.Contains("enum class TestState", generatedText, StringComparison.Ordinal);
            Assert.Contains("RegisterState", generatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"TState.hpp\"", generatedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the converter against one temporary project fixture and returns the generated output bundle.
        /// </summary>
        /// <param name="source">Single C# source file content to convert.</param>
        /// <returns>Generated output bundle for assertions.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-fsm-tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(rootPath, "Fixture.csproj");
            string sourcePath = Path.Combine(rootPath, "Fixture.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(rootPath);
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <LangVersion>preview</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(sourcePath, source);

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;

            CPPCodeConverter converter = new CPPCodeConverter(new CPPConversionRules(), options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            return new ConversionOutput(
                outputPath,
                string.Join(
                    "\n",
                    Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
                        .Where(path => path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .Select(File.ReadAllText)),
                JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "cpp-conversion-report.json"))));
        }

        /// <summary>
        /// Stores one generated output bundle used by the converter audit.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        /// <param name="GeneratedText">Concatenated generated C++ text.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}
