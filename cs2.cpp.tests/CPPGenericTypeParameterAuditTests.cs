using System.Text.Json;
using cs2.cpp;

namespace cs2.cpp.tests {
    /// <summary>
    /// Covers generic type-parameter emission so compile-time template symbols do not leak into generated includes.
    /// </summary>
    public class CPPGenericTypeParameterAuditTests {
        /// <summary>
        /// Ensures interface type parameters remain compile-time template symbols and do not generate header includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericInterfaceMethod_DoesNotEmitGenericParameterInclude() {
            string source = """
                public class Stream {
                }

                public interface IContentProcessor {
                    object ReadObject(Stream stream);
                }

                public interface IContentProcessor<T> : IContentProcessor {
                    T Read(Stream stream);
                }
                """;

            ConversionOutput output = RunConversion(source);
            string baseInterfaceHeader = File.ReadAllText(Path.Combine(output.OutputPath, "IContentProcessor.hpp"));
            string genericInterfaceHeader = File.ReadAllText(Path.Combine(output.OutputPath, "IContentProcessor_1.hpp"));

            Assert.Contains("class IContentProcessor", baseInterfaceHeader);
            Assert.Contains("template <typename T>", genericInterfaceHeader);
            Assert.Contains("class IContentProcessor_1", genericInterfaceHeader);
            Assert.DoesNotContain(": public IContentProcessor_1", genericInterfaceHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"T.hpp\"", genericInterfaceHeader, StringComparison.Ordinal);
            Assert.Contains("#include \"IContentProcessor.hpp\"", genericInterfaceHeader);
            Assert.Contains("class Stream;", genericInterfaceHeader);
            Assert.DoesNotContain("#include \"Stream.hpp\"", genericInterfaceHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures method-level generic parameters remain compile-time template symbols and do not generate header includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericMethodOnNonGenericClass_DoesNotEmitGenericParameterInclude() {
            string source = """
                public class Stream {
                }

                public interface IContentProcessor<T> {
                    T Read(Stream stream);
                }

                public class ContentManager {
                    public T Load<T>(string assetPath, IContentProcessor<T> processor) {
                        return processor.Read(new Stream());
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string classHeader = File.ReadAllText(Path.Combine(output.OutputPath, "ContentManager.hpp"));

            Assert.DoesNotContain("#include \"T.hpp\"", classHeader, StringComparison.Ordinal);
            Assert.Contains("template <typename T>", classHeader);
            Assert.Contains("Load", classHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures pointer-qualified generic parameters stay as template symbols instead of becoming invalid include paths.
        /// </summary>
        [Fact]
        public void WriteOutput_WithPointerQualifiedGenericParameter_DoesNotEmitPointerPseudoInclude() {
            string source = """
                public unsafe struct Buffer<T> where T : unmanaged {
                    public T* Memory;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string bufferHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Buffer_1.hpp"));

            Assert.DoesNotContain("#include \"T*.hpp\"", bufferHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures unsafe pointer members preserve their pointed element types instead of degrading to object pointers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithUnsafePointerMembers_PreservesPointerTypes() {
            string source = """
                public unsafe struct Buffer<T> where T : unmanaged {
                    public T* Memory;

                    public Buffer(void* memory, int length) {
                    }

                    public T* GetPointer(int index) {
                        return Memory;
                    }
                }

                public unsafe struct PowerPool {
                    public byte*[] Blocks;

                    public byte* GetStartPointerForSlot(int slot) {
                        return Blocks[slot];
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string bufferHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Buffer_1.hpp"));
            string powerPoolHeader = File.ReadAllText(Path.Combine(output.OutputPath, "PowerPool.hpp"));

            Assert.Contains("T* Memory;", bufferHeader, StringComparison.Ordinal);
            Assert.Contains("Buffer_1(void* memory, int32_t length);", bufferHeader, StringComparison.Ordinal);
            Assert.Contains("T* GetPointer(int32_t index);", bufferHeader, StringComparison.Ordinal);
            Assert.Contains("Array<uint8_t*>* Blocks;", powerPoolHeader, StringComparison.Ordinal);
            Assert.Contains("uint8_t* GetStartPointerForSlot(int32_t slot);", powerPoolHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("object*", output.GeneratedText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures class-level generic parameters remain compile-time symbols and do not generate pseudo includes.
        /// </summary>
        [Fact]
        public void WriteOutput_WithClassGenericParameter_DoesNotEmitGenericParameterInclude() {
            string source = """
                public struct Buffer<T> {
                    public T Value;
                }
                """;

            ConversionOutput output = RunConversion(source);
            string bufferHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Buffer_1.hpp"));
            string bufferSource = File.ReadAllText(Path.Combine(output.OutputPath, "Buffer_1.cpp"));

            Assert.DoesNotContain("#include \"T.hpp\"", bufferHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("#include \"T.hpp\"", bufferSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic interface methods are lowered through the sole concrete implementation instead of emitting illegal virtual member templates.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericInterfaceInvocation_UsesConcreteImplementationDispatch() {
            string source = """
                public interface IEcho {
                    T Echo<T>(T value);
                }

                public class EchoImpl : IEcho {
                    public T Echo<T>(T value) {
                        return value;
                    }
                }

                public class EchoCaller {
                    public int Invoke(IEcho echo) {
                        return echo.Echo<int>(123);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string interfaceHeader = File.ReadAllText(Path.Combine(output.OutputPath, "IEcho.hpp"));
            string callerSource = File.ReadAllText(Path.Combine(output.OutputPath, "EchoCaller.cpp"));

            Assert.DoesNotContain("virtual T Echo", interfaceHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("= 0;", interfaceHeader, StringComparison.Ordinal);
            Assert.Contains("static_cast<EchoImpl*>(echo)->Echo(123)", callerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("echo->Echo(123)", callerSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures rewritten generic interface invocations preserve emitted ref and out suffixes on the concrete implementation target.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericInterfaceRefOutInvocation_UsesEmittedModifierSuffixes() {
            string source = """
                public struct Buffer<T> {
                    public T Value;
                }

                public interface IBufferPool {
                    void Return<T>(ref Buffer<T> buffer);
                    void Take<T>(int count, out Buffer<T> buffer);
                }

                public class BufferPool : IBufferPool {
                    public void Return<T>(ref Buffer<T> buffer) {
                        buffer = new Buffer<T>();
                    }

                    public void Take<T>(int count, out Buffer<T> buffer) {
                        buffer = new Buffer<T>();
                    }
                }

                public class BufferUser {
                    public void Free(IBufferPool pool, ref Buffer<int> buffer) {
                        pool.Return(ref buffer);
                    }

                    public Buffer<int> Create(IBufferPool pool) {
                        pool.Take(4, out var buffer);
                        return buffer;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string userSource = File.ReadAllText(Path.Combine(output.OutputPath, "BufferUser.cpp"));

            Assert.Contains("static_cast<BufferPool*>(pool)->Return__ref0<int32_t>(buffer)", userSource, StringComparison.Ordinal);
            Assert.Contains("static_cast<BufferPool*>(pool)->Take__out1(4, buffer)", userSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures abstract generic methods do not emit illegal virtual member templates and dispatch through the sole concrete implementation.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAbstractGenericInvocation_UsesConcreteImplementationDispatch() {
            string source = """
                public abstract class EchoBase {
                    public abstract T Echo<T>(T value);
                }

                public class EchoImpl : EchoBase {
                    public override T Echo<T>(T value) {
                        return value;
                    }
                }

                public class EchoCaller {
                    public int Invoke(EchoBase echo) {
                        return echo.Echo<int>(123);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string baseHeader = File.ReadAllText(Path.Combine(output.OutputPath, "EchoBase.hpp"));
            string callerSource = File.ReadAllText(Path.Combine(output.OutputPath, "EchoCaller.cpp"));

            Assert.DoesNotContain("virtual T Echo", baseHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("= 0;", baseHeader, StringComparison.Ordinal);
            Assert.Contains("static_cast<EchoImpl*>(echo)->Echo(123)", callerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("echo->Echo(123)", callerSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures abstract generic invocations with multiple concrete implementations lower to a runtime dispatch chain instead of unresolved base-template calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAbstractGenericInvocationAndMultipleImplementations_UsesRuntimeDispatchChain() {
            string source = """
                public abstract class EchoBase {
                    public abstract T Echo<T>(T value);
                }

                public class EchoImplA : EchoBase {
                    public override T Echo<T>(T value) {
                        return value;
                    }
                }

                public class EchoImplB : EchoBase {
                    public override T Echo<T>(T value) {
                        return value;
                    }
                }

                public class EchoFactory {
                    public EchoBase Create(bool useB) {
                        if (useB) {
                            return new EchoImplB();
                        }

                        return new EchoImplA();
                    }
                }

                public class EchoCaller {
                    public int Invoke(EchoBase echo) {
                        return echo.Echo<int>(123);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string callerSource = File.ReadAllText(Path.Combine(output.OutputPath, "EchoCaller.cpp"));

            Assert.Contains("dynamic_cast<::EchoImplA*>(echo)", callerSource, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::EchoImplB*>(echo)", callerSource, StringComparison.Ordinal);
            Assert.Contains("#include \"EchoImplA.hpp\"", callerSource, StringComparison.Ordinal);
            Assert.Contains("#include \"EchoImplB.hpp\"", callerSource, StringComparison.Ordinal);
            Assert.Contains("heCppDispatchImpl->Echo(123)", callerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("echo->Echo<int32_t>(123)", callerSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures abstract generic dispatch still works when the concrete implementation is inherited from an intermediate generic base instead of being declared directly on the instantiated leaf type.
        /// </summary>
        [Fact]
        public void WriteOutput_WithInheritedGenericBaseImplementation_UsesRuntimeDispatchChain() {
            string source = """
                public abstract class EchoBase {
                    public abstract T Echo<T>(T value);
                }

                public abstract class EchoMid<TState> : EchoBase {
                    public override T Echo<T>(T value) {
                        return value;
                    }
                }

                public sealed class EchoImplA : EchoMid<int> {
                }

                public sealed class EchoImplB : EchoMid<string> {
                }

                public class EchoFactory {
                    public EchoBase Create(bool useB) {
                        if (useB) {
                            return new EchoImplB();
                        }

                        return new EchoImplA();
                    }
                }

                public class EchoCaller {
                    public int Invoke(EchoBase echo) {
                        return echo.Echo<int>(123);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string callerSource = File.ReadAllText(Path.Combine(output.OutputPath, "EchoCaller.cpp"));

            Assert.Contains("dynamic_cast<::EchoImplA*>(echo)", callerSource, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::EchoImplB*>(echo)", callerSource, StringComparison.Ordinal);
            Assert.Contains("heCppDispatchImpl->Echo(123)", callerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("echo->Echo<int32_t>(123)", callerSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures explicit base-qualified generic calls remain statically bound to the base implementation instead of being rewritten into abstract runtime dispatch.
        /// </summary>
        [Fact]
        public void WriteOutput_WithBaseQualifiedGenericInvocation_DoesNotRewriteToRuntimeDispatch() {
            string source = """
                public abstract class TypeProcessor<TReference, TPrestepData, TAccumulatedImpulse> {
                    protected virtual void GenerateSortKeysAndCopyReferences<TSortKeyGenerator>(ref int value) {
                    }
                }

                public abstract class OneBodyTypeProcessor<TPrestepData, TAccumulatedImpulse, TConstraintFunctions>
                    : TypeProcessor<int, TPrestepData, TAccumulatedImpulse> {
                    struct OneBodySortKeyGenerator {
                    }

                    public void InvokeBase(ref int value) {
                        base.GenerateSortKeysAndCopyReferences<OneBodySortKeyGenerator>(ref value);
                    }
                }

                public sealed class ProcessorA : OneBodyTypeProcessor<int, int, int> {
                }

                public sealed class ProcessorB : OneBodyTypeProcessor<float, float, float> {
                }
                """;

            ConversionOutput output = RunConversion(source);
            string processorSource = File.ReadAllText(Path.Combine(output.OutputPath, "OneBodyTypeProcessor_3.cpp"));

            Assert.Contains("TypeProcessor_3<int32_t, TPrestepData, TAccumulatedImpulse>::template GenerateSortKeysAndCopyReferences", processorSource, StringComparison.Ordinal);
            Assert.DoesNotContain("heCppDispatchReceiver", processorSource, StringComparison.Ordinal);
            Assert.DoesNotContain("dynamic_cast<::ProcessorA*>", processorSource, StringComparison.Ordinal);
            Assert.DoesNotContain("dynamic_cast<::ProcessorB*>", processorSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures runtime-dispatch rewrites preserve explicit generic type arguments instead of dropping them from the concrete implementation call.
        /// </summary>
        [Fact]
        public void WriteOutput_WithExplicitGenericDispatchInvocation_PreservesTypeArguments() {
            string source = """
                public abstract class WorkerBase {
                    public abstract void Execute<TValue, TMode, TFlag>(TValue value);
                }

                public sealed class WorkerA : WorkerBase {
                    public override void Execute<TValue, TMode, TFlag>(TValue value) {
                    }
                }

                public sealed class WorkerB : WorkerBase {
                    public override void Execute<TValue, TMode, TFlag>(TValue value) {
                    }
                }

                public static class WorkerFactory {
                    public static WorkerBase Create(bool useB) {
                        if (useB) {
                            return new WorkerB();
                        }

                        return new WorkerA();
                    }
                }

                public class Caller {
                    public void Invoke<TValue, TFlag>(bool useB, TValue value) {
                        WorkerBase worker = WorkerFactory.Create(useB);
                        worker.Execute<TValue, int, TFlag>(value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string callerSource = File.ReadAllText(Path.Combine(output.OutputPath, "Caller.cpp"));

            Assert.Contains("dynamic_cast<::WorkerA*>(worker)", callerSource, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::WorkerB*>(worker)", callerSource, StringComparison.Ordinal);
            Assert.Contains("heCppDispatchImpl->Execute<TValue, int32_t, TFlag>(value)", callerSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures abstract generic self-calls inside an abstract base use runtime dispatch when multiple concrete implementations exist.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAbstractGenericSelfInvocationAndMultipleImplementations_UsesRuntimeDispatchChain() {
            string source = """
                public abstract class EchoBase {
                    public T Forward<T>(T value) {
                        return Echo<T>(value);
                    }

                    protected abstract T Echo<T>(T value);
                }

                public class EchoImplA : EchoBase {
                    protected override T Echo<T>(T value) {
                        return value;
                    }
                }

                public class EchoImplB : EchoBase {
                    protected override T Echo<T>(T value) {
                        return value;
                    }
                }

                public class EchoFactory {
                    public EchoBase Create(bool useB) {
                        if (useB) {
                            return new EchoImplB();
                        }

                        return new EchoImplA();
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string baseSource = File.ReadAllText(Path.Combine(output.OutputPath, "EchoBase.cpp"));

            Assert.Contains("dynamic_cast<::EchoImplA*>(this)", baseSource, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::EchoImplB*>(this)", baseSource, StringComparison.Ordinal);
            Assert.Contains("#include \"EchoImplA.hpp\"", baseSource, StringComparison.Ordinal);
            Assert.Contains("#include \"EchoImplB.hpp\"", baseSource, StringComparison.Ordinal);
            Assert.Contains("heCppDispatchImpl->Echo(value)", baseSource, StringComparison.Ordinal);
            Assert.DoesNotContain("this->Echo<T>(value)", baseSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures abstract generic methods without reachable concrete implementations still emit one throwing native definition so callers do not fail at link time.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAbstractGenericMethodWithoutImplementations_EmitsThrowingDefinition() {
            string source = """
                public abstract class SweepBase {
                    public void Run<T>(T value) {
                        Execute<T>(value);
                    }

                    protected abstract void Execute<T>(T value);
                }
                """;

            ConversionOutput output = RunConversion(source);
            string baseSource = File.ReadAllText(Path.Combine(output.OutputPath, "SweepBase.cpp"));

            Assert.Contains("template <typename T>", baseSource, StringComparison.Ordinal);
            Assert.Contains("void SweepBase::Execute(T value)", baseSource, StringComparison.Ordinal);
            Assert.Contains("throw new NotSupportedException(\"Method has no generated body.\");", baseSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures multi-implementation runtime dispatch preserves `ref this` value-type arguments instead of degrading them to native pointers.
        /// </summary>
        [Fact]
        public void WriteOutput_WithAbstractGenericInvocationAndRefThisArgument_PreservesValueTypeReceiverArgument() {
            string source = """
                public abstract class TaskBase {
                    public abstract void Execute<T>(ref int value, ref Worker<T> worker);
                }

                public class TaskA : TaskBase {
                    public override void Execute<T>(ref int value, ref Worker<T> worker) {
                        value++;
                    }
                }

                public class TaskB : TaskBase {
                    public override void Execute<T>(ref int value, ref Worker<T> worker) {
                        value += 2;
                    }
                }

                public struct Worker<T> {
                    public TaskBase Task;

                    public void Run(ref int value) {
                        Task.Execute<T>(ref value, ref this);
                    }
                }

                public class WorkerFactory {
                    public Worker<int> Create(bool useB) {
                        var worker = new Worker<int>();
                        worker.Task = useB ? new TaskB() : new TaskA();
                        return worker;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string workerSource = File.ReadAllText(Path.Combine(output.OutputPath, "Worker_1.cpp"));

            Assert.Contains("dynamic_cast<::TaskA*>(this->Task)", workerSource, StringComparison.Ordinal);
            Assert.Contains("dynamic_cast<::TaskB*>(this->Task)", workerSource, StringComparison.Ordinal);
            Assert.Contains("heCppDispatchImpl->Execute__ref0_ref1(value, (*this))", workerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("heCppDispatchImpl->Execute__ref0_ref1(value, this)", workerSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures protected generic overrides that require runtime dispatch are emitted as callable public native members.
        /// </summary>
        [Fact]
        public void WriteOutput_WithProtectedGenericOverride_EmitsPublicNativeDeclaration() {
            string source = """
                public abstract class EchoBase {
                    public T Forward<T>(T value) {
                        return Echo<T>(value);
                    }

                    protected abstract T Echo<T>(T value);
                }

                public class EchoImplA : EchoBase {
                    protected override T Echo<T>(T value) {
                        return value;
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string derivedHeader = File.ReadAllText(Path.Combine(output.OutputPath, "EchoImplA.hpp"));

            Assert.Contains("public:", derivedHeader, StringComparison.Ordinal);
            Assert.Contains("template <typename T>", derivedHeader, StringComparison.Ordinal);
            Assert.Contains("T Echo(T value);", derivedHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("protected:\r\n    template <typename T>\r\n    T Echo(T value);", derivedHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures single-implementation generic dispatch records the concrete generated type dependency so the caller header includes the implementation declaration required by emitted static_cast calls.
        /// </summary>
        [Fact]
        public void WriteOutput_WithSingleImplementationGenericDispatch_IncludesConcreteImplementationHeader() {
            string source = """
                public abstract class TaskBase {
                    public abstract void Execute<T>(ref int value);
                }

                public class TaskImpl : TaskBase {
                    public override void Execute<T>(ref int value) {
                        value++;
                    }
                }

                public class Worker {
                    public TaskBase Task;

                    public void Run(ref int value) {
                        Task.Execute<int>(ref value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string workerSource = File.ReadAllText(Path.Combine(output.OutputPath, "Worker.cpp"));

            Assert.Contains("#include \"TaskImpl.hpp\"", workerSource, StringComparison.Ordinal);
            Assert.Contains("static_cast<TaskImpl*>(this->Task)->Execute__ref0(value)", workerSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures generic implementation types do not lower to unspecialized static_cast targets when invoked through an abstract generic base contract.
        /// </summary>
        [Fact]
        public void WriteOutput_WithGenericImplementationDispatch_DoesNotEmitUnspecializedStaticCast() {
            string source = """
                public abstract class TaskBase {
                    public abstract void Execute<T>(ref int value);
                }

                public class TaskImpl<TState> : TaskBase {
                    public override void Execute<T>(ref int value) {
                        value++;
                    }
                }

                public class Worker {
                    public TaskBase Task;

                    public void Run(ref int value) {
                        Task.Execute<int>(ref value);
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string workerSource = File.ReadAllText(Path.Combine(output.OutputPath, "Worker.cpp"));

            Assert.DoesNotContain("static_cast<TaskImpl_1*>", workerSource, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures nested types declared inside generic outer types inherit the outer generic parameters instead of emitting pseudo includes for captured type parameters.
        /// </summary>
        [Fact]
        public void WriteOutput_WithNestedTypeCapturingOuterGenericParameter_DoesNotEmitGenericParameterInclude() {
            string source = """
                using System.Collections.Generic;

                public class Outer<T> {
                    public Enumerator GetEnumerator() {
                        return new Enumerator();
                    }

                    public struct Enumerator : IEnumerator<T> {
                        public T Current => default;

                        object System.Collections.IEnumerator.Current => Current;

                        public void Dispose() {
                        }

                        public bool MoveNext() {
                            return false;
                        }

                        public void Reset() {
                        }
                    }
                }
                """;

            ConversionOutput output = RunConversion(source);
            string enumeratorHeader = Directory.GetFiles(output.OutputPath, "*Enumerator*.hpp", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText)
                .Single(content => content.Contains("class Enumerator", StringComparison.Ordinal));
            string outerHeader = File.ReadAllText(Path.Combine(output.OutputPath, "Outer_1.hpp"));

            Assert.Contains("template <typename T>", enumeratorHeader);
            Assert.DoesNotContain("#include \"T.hpp\"", enumeratorHeader, StringComparison.Ordinal);
            Assert.Contains("Enumerator_1<T> GetEnumerator();", outerHeader);
            Assert.DoesNotContain("Enumerator GetEnumerator();", outerHeader, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runs the C++ converter against a temporary single-file project and returns the generated output bundle.
        /// </summary>
        /// <param name="source">C# source file content to convert.</param>
        /// <returns>Output folder, parsed report, and generated textual output.</returns>
        static ConversionOutput RunConversion(string source) {
            string rootPath = Path.Combine(Path.GetTempPath(), "cs2cpp-generic-parameter-tests", Guid.NewGuid().ToString("N"));
            string projectPath = Path.Combine(rootPath, "Fixture.csproj");
            string sourcePath = Path.Combine(rootPath, "Fixture.cs");
            string outputPath = Path.Combine(rootPath, "out");

            Directory.CreateDirectory(rootPath);
            File.WriteAllText(projectPath, CreateProjectFile());
            File.WriteAllText(sourcePath, source);

            CPPConversionOptions options = CPPConversionOptions.CreateDefault();
            options.LoadNativeRuntimeMetadata = false;
            options.WriteConversionReport = true;

            CPPConversionRules rules = new CPPConversionRules();
            CPPCodeConverter converter = new CPPCodeConverter(rules, options);
            converter.AddCsproj(projectPath);
            converter.WriteOutput(outputPath);

            string reportPath = Path.Combine(outputPath, "cpp-conversion-report.json");
            JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
            string generatedText = ReadGeneratedOutput(outputPath);
            return new ConversionOutput(outputPath, generatedText, report);
        }

        /// <summary>
        /// Creates a minimal SDK-style project file for temporary converter fixtures.
        /// </summary>
        /// <returns>Project file content suitable for Roslyn-based analysis.</returns>
        static string CreateProjectFile() {
            return """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <LangVersion>preview</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                  </PropertyGroup>
                </Project>
                """;
        }

        /// <summary>
        /// Reads all generated headers and sources from a converter output directory into a single string for assertions.
        /// </summary>
        /// <param name="outputPath">Converter output directory to inspect.</param>
        /// <returns>Concatenated generated text content.</returns>
        static string ReadGeneratedOutput(string outputPath) {
            string[] files = Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return string.Join("\n", files.Select(File.ReadAllText));
        }

        /// <summary>
        /// Represents the generated output artifacts captured for a generic type-parameter fixture.
        /// </summary>
        /// <param name="OutputPath">Generated output directory.</param>
        /// <param name="GeneratedText">Concatenated generated textual output.</param>
        /// <param name="Report">Parsed conversion report.</param>
        record ConversionOutput(string OutputPath, string GeneratedText, JsonDocument Report);
    }
}
