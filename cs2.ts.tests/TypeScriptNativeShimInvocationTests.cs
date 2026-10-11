using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies that Roslyn-bound BCL calls resolve through their native TypeScript runtime shims.</summary>
    public sealed class TypeScriptNativeShimInvocationTests {
        /// <summary>Checks the separate lowercase string receiver and boxed String static helper aliases.</summary>
        [Fact]
        public void StringInstanceAndStaticCharacterCallsUseActualShimAliases() {
            const string source = "class Caller { public static string Call(string value) => value.Substring(1); public static bool Valid(char value) => char.IsLetterOrDigit(value); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var calls = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToDictionary(node => node.Expression.ToString());
            var callerDeclaration = compilation.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            var callerType = (INamedTypeSymbol)compilation.Model.GetDeclaredSymbol(callerDeclaration)!;
            var harness = CreateHarness(callerType, "Caller", "Call", "string");
            Assert.NotNull(compilation.Model.GetSymbolInfo(calls["value.Substring"]).Symbol);
            var receiverExpression = ((MemberAccessExpressionSyntax)calls["value.Substring"].Expression).Expression;
            var receiverType = compilation.Model.GetTypeInfo(receiverExpression).Type;
            var receiverShim = TypeScriptConversiorProcessor.GetClass(harness.Program, VariableUtil.GetVarType(receiverType));
            Assert.Equal("string", receiverShim?.Name);
            Assert.Contains(receiverShim!.Functions, function => function.Name == "Substring");
            var methodName = ((MemberAccessExpressionSyntax)calls["value.Substring"].Expression).Name;
            var boundMethod = compilation.Model.GetSymbolInfo(methodName).Symbol as IMethodSymbol;
            Assert.NotNull(boundMethod);
            Assert.Single(boundMethod!.Parameters);
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, calls["value.Substring"], output);

            Assert.Equal("value.substring(1)", TsProcessorTestHarness.JoinLines(output).Trim());
            output.Clear();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, calls["char.IsLetterOrDigit"], output);

            Assert.Equal("NativeStringUtil.isLetterOrDigit(value)", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        /// <summary>Checks an inherited hash API maps to its lowercase runtime method despite a caller prefix distractor.</summary>
        [Fact]
        public void InheritedHashAlgorithmCallUsesRuntimeShimWithoutCallerPrefixCapture() {
            const string source = "class HashAlgorithm { public byte[] ComputeHash(byte[] data) => data; } class HMACSHA256 : HashAlgorithm { } class Caller { public static byte[] Call(HMACSHA256 algorithm, byte[] data) => algorithm.ComputeHash(data); private static byte[] ComputeHashSignature(HMACSHA256 algorithm, byte[] data) => data; }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(node => node.Expression.ToString() == "algorithm.ComputeHash");
            var callerDeclaration = compilation.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Caller");
            var callerType = (INamedTypeSymbol)compilation.Model.GetDeclaredSymbol(callerDeclaration)!;
            var harness = CreateHarness(callerType, "Caller", "Call", "byte[]");
            var hashAlgorithmShim = new ConversionClass { Name = "HashAlgorithm" };
            hashAlgorithmShim.Functions.Add(new ConversionFunction {
                Name = "computeHash",
                ReturnType = VariableUtil.GetVarType("byte[]"),
                InParameters = new List<ConversionVariable> { new ConversionVariable { Name = "data", VarType = VariableUtil.GetVarType("byte[]") } }
            });
            harness.Program.RegisterClass(hashAlgorithmShim);
            harness.Program.RegisterClass(new ConversionClass { Name = "HMACSHA256" });
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("algorithm.computeHash(data)", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        /// <summary>Checks reduced reader and writer extensions dispatch through their receiver shims.</summary>
        [Fact]
        public void ReducedReaderAndWriterExtensionsResolveOnReceiverShims() {
            const string source = "class BinaryReader { } class BinaryWriter { } static class BinaryReaderExtensions { public static byte[] ReadArray(this BinaryReader reader) => new byte[0]; } static class BinaryWriterExtensions { public static void WriteArray(this BinaryWriter writer, byte[] array) { } } class Caller { public static byte[] Read(BinaryReader reader) => reader.ReadArray(); private static byte[] ReadArraySignature(BinaryReader reader) => new byte[0]; public static void Write(BinaryWriter writer, byte[] data) => writer.WriteArray(data); private static void WriteArraySignature(BinaryWriter writer, byte[] data) { } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var calls = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToDictionary(node => node.Expression.ToString());
            var callerDeclaration = compilation.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Caller");
            var callerType = (INamedTypeSymbol)compilation.Model.GetDeclaredSymbol(callerDeclaration)!;
            var harness = CreateHarness(callerType, "Caller", "Read", "byte[]");
            var readerShim = new ConversionClass { Name = "BinaryReader" };
            readerShim.Functions.Add(new ConversionFunction { Name = "readArray", ReturnType = VariableUtil.GetVarType("byte[]"), InParameters = new List<ConversionVariable>() });
            harness.Program.RegisterClass(readerShim);
            var writerShim = new ConversionClass { Name = "BinaryWriter" };
            writerShim.Functions.Add(new ConversionFunction {
                Name = "writeArray",
                ReturnType = VariableUtil.GetVarType("void"),
                InParameters = new List<ConversionVariable> { new ConversionVariable { Name = "array", VarType = VariableUtil.GetVarType("byte[]") } }
            });
            harness.Program.RegisterClass(writerShim);
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, calls["reader.ReadArray"], output);

            Assert.Equal("reader.readArray()", TsProcessorTestHarness.JoinLines(output).Trim());
            output.Clear();
            harness.Processor.ProcessExpression(compilation.Model, harness.Context, calls["writer.WriteArray"], output);

            Assert.Equal("writer.writeArray(data)", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        /// <summary>Creates the conversion context with the native primitive remaps and the current source class.</summary>
        static ProcessorHarness CreateHarness(INamedTypeSymbol callerType, string className, string functionName, string returnType) {
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, className, functionName, VariableUtil.GetVarType(returnType));
            harness.Context.GetCurrentClass()!.TypeSymbol = callerType;
            var nativeRemap = typeof(TypeScriptProgram).GetMethod("buildNativeRemap", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nativeRemap);
            nativeRemap.Invoke(harness.Program, null);
            return new ProcessorHarness(harness.Processor, harness.Context, harness.Program);
        }

        /// <summary>Holds the processor, context, and program used by the conversion regression cases.</summary>
        private sealed class ProcessorHarness {
            /// <summary>Gets the syntax conversion processor.</summary>
            public TypeScriptConversiorProcessor Processor { get; }
            /// <summary>Gets the active conversion context.</summary>
            public TypeScriptLayerContext Context { get; }
            /// <summary>Gets the conversion program with its registered classes.</summary>
            public TypeScriptProgram Program { get; }

            /// <summary>Creates a holder for the conversion components.</summary>
            /// <param name="processor">The syntax converter.</param>
            /// <param name="context">The current conversion context.</param>
            /// <param name="program">The conversion program.</param>
            public ProcessorHarness(TypeScriptConversiorProcessor processor, TypeScriptLayerContext context, TypeScriptProgram program) {
                Processor = processor;
                Context = context;
                Program = program;
            }
        }
    }
}