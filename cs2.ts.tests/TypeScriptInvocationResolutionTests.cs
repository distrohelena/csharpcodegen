using System.Collections.Generic;
using System.Linq;
using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public sealed class TypeScriptInvocationResolutionTests {
        [Fact]
        public void StaticCallOnDifferentNamespaceTypeDoesNotSelectCallerOverloadWithSamePrefix() {
            const string source = "namespace Crypto { public static class MLDsaKey { public static bool Verify(byte[] message, byte[] signature, byte[] publicKey) => true; } } namespace Rewards { public static class MLDsaKey { public static bool Verify(object certificate, long now) => true; private static bool VerifySignature(byte[] publicKey, byte[] signature, byte[] signedBytes) => true; public static bool Call() => Crypto.MLDsaKey.Verify(new byte[0], new byte[0], new byte[0]); } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.Expression.ToString() == "Crypto.MLDsaKey.Verify");
            var callerDeclaration = compilation.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "MLDsaKey" && compilation.Model.GetDeclaredSymbol(node)?.ContainingNamespace.ToDisplayString() == "Rewards");
            var callerType = (INamedTypeSymbol)compilation.Model.GetDeclaredSymbol(callerDeclaration)!;
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, className: "MLDsaKey", functionName: "Call", returnType: VariableUtil.GetVarType("bool"));
            var callerClass = harness.Context.GetCurrentClass() ?? throw new System.InvalidOperationException("The test caller class was not registered.");
            callerClass.TypeSymbol = callerType;
            callerClass.Functions.Add(new ConversionFunction {
                Name = "Verify",
                IsStatic = true,
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "certificate", VarType = VariableUtil.GetVarType("object") },
                    new ConversionVariable { Name = "now", VarType = VariableUtil.GetVarType("long") }
                }
            });
            callerClass.Functions.Add(new ConversionFunction {
                Name = "VerifySignature",
                IsStatic = true,
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "publicKey", VarType = VariableUtil.GetVarType("byte[]") },
                    new ConversionVariable { Name = "signature", VarType = VariableUtil.GetVarType("byte[]") },
                    new ConversionVariable { Name = "signedBytes", VarType = VariableUtil.GetVarType("byte[]") }
                }
            });
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("MLDsaKey.Verify(new Uint8Array(0), new Uint8Array(0), new Uint8Array(0))", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        [Fact]
        public void StaticCallOnProgramKnownExternalClassUsesItsOwnRegisteredRemap() {
            const string source = "namespace External { public static class MLDsaKey { public static bool Verify(byte[] message, byte[] signature, byte[] publicKey) => true; } } namespace Caller { public static class Verifier { public static bool Verify(object certificate, long now) => true; private static bool VerifySignature(byte[] publicKey, byte[] signature, byte[] signedBytes) => true; public static bool Call() => External.MLDsaKey.Verify(new byte[0], new byte[0], new byte[0]); } }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.Expression.ToString() == "External.MLDsaKey.Verify");
            var callerDeclaration = compilation.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "Verifier");
            var callerType = (INamedTypeSymbol)compilation.Model.GetDeclaredSymbol(callerDeclaration)!;
            var externalType = compilation.Compilation.GetTypeByMetadataName("External.MLDsaKey")!;
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, className: "Verifier", functionName: "Call", returnType: VariableUtil.GetVarType("bool"));
            var callerClass = harness.Context.GetCurrentClass() ?? throw new System.InvalidOperationException("The test caller class was not registered.");
            callerClass.TypeSymbol = callerType;
            callerClass.Functions.Add(new ConversionFunction {
                Name = "Verify",
                IsStatic = true,
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "certificate", VarType = VariableUtil.GetVarType("object") },
                    new ConversionVariable { Name = "now", VarType = VariableUtil.GetVarType("long") }
                }
            });
            callerClass.Functions.Add(new ConversionFunction {
                Name = "VerifySignature",
                IsStatic = true,
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "publicKey", VarType = VariableUtil.GetVarType("byte[]") },
                    new ConversionVariable { Name = "signature", VarType = VariableUtil.GetVarType("byte[]") },
                    new ConversionVariable { Name = "signedBytes", VarType = VariableUtil.GetVarType("byte[]") }
                }
            });
            var knownExternalClass = new ConversionClass { Name = "MLDsaKey", TypeSymbol = externalType };
            knownExternalClass.Functions.Add(new ConversionFunction {
                Name = "Verify",
                Remap = "VerifyCompact",
                IsStatic = true,
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "message", VarType = VariableUtil.GetVarType("byte[]") },
                    new ConversionVariable { Name = "signature", VarType = VariableUtil.GetVarType("byte[]") },
                    new ConversionVariable { Name = "publicKey", VarType = VariableUtil.GetVarType("byte[]") }
                }
            });
            harness.Program.RegisterClass(knownExternalClass);
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("MLDsaKey.VerifyCompact(new Uint8Array(0), new Uint8Array(0), new Uint8Array(0))", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        [Fact]
        public void StaticCallOnCurrentClassStillUsesItsRegisteredRemap() {
            const string source = "class MLDsaKey { public static bool Verify(byte[] message, byte[] signature, byte[] publicKey) => true; public static bool Call() => Verify(new byte[0], new byte[0], new byte[0]); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(node => node.Expression.ToString() == "Verify");
            var callerDeclaration = compilation.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            var callerType = (INamedTypeSymbol)compilation.Model.GetDeclaredSymbol(callerDeclaration)!;
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, className: "MLDsaKey", functionName: "Call", returnType: VariableUtil.GetVarType("bool"));
            var callerClass = harness.Context.GetCurrentClass() ?? throw new System.InvalidOperationException("The test caller class was not registered.");
            callerClass.TypeSymbol = callerType;
            callerClass.Functions.Add(new ConversionFunction {
                Name = "Verify",
                Remap = "VerifySignature",
                IsStatic = true,
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "message", VarType = VariableUtil.GetVarType("byte[]") },
                    new ConversionVariable { Name = "signature", VarType = VariableUtil.GetVarType("byte[]") },
                    new ConversionVariable { Name = "publicKey", VarType = VariableUtil.GetVarType("byte[]") }
                }
            });
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context,
                invocation, output);

            Assert.Equal("MLDsaKey.VerifySignature(new Uint8Array(0), new Uint8Array(0), new Uint8Array(0))", TsProcessorTestHarness.JoinLines(output).Trim());
        }

        [Fact]
        public void FrameworkStringCallUsesRegisteredStringShimRemap() {
            const string source = "class Caller { public static string Call(string value) => value.Substring(1); }";
            var compilation = RoslynTestHelper.CreateCompilation(source);
            var invocation = compilation.Root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var callerDeclaration = compilation.Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            var callerType = (INamedTypeSymbol)compilation.Model.GetDeclaredSymbol(callerDeclaration)!;
            var harness = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(harness.Context, className: "Caller", functionName: "Call", returnType: VariableUtil.GetVarType("string"));
            var callerClass = harness.Context.GetCurrentClass() ?? throw new System.InvalidOperationException("The test caller class was not registered.");
            callerClass.TypeSymbol = callerType;
            callerClass.Functions.Add(new ConversionFunction {
                Name = "Call",
                IsStatic = true,
                ReturnType = VariableUtil.GetVarType("string"),
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "value", VarType = VariableUtil.GetVarType("string") }
                }
            });
            var stringShim = new ConversionClass { Name = "string" };
            stringShim.Functions.Add(new ConversionFunction {
                Name = "Substring",
                Remap = "substring",
                ReturnType = VariableUtil.GetVarType("string"),
                InParameters = new List<ConversionVariable> {
                    new ConversionVariable { Name = "startIndex", VarType = VariableUtil.GetVarType("int") }
                }
            });
            harness.Program.RegisterClass(stringShim);
            var output = new List<string>();

            harness.Processor.ProcessExpression(compilation.Model, harness.Context, invocation, output);

            Assert.Equal("value.substring(1)", TsProcessorTestHarness.JoinLines(output).Trim());
        }
    }
}
