using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using cs2.core;
using cs2.ts;
using cs2.ts.util;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    public class TypeScriptReflectionIntegrationTests {
        static readonly MethodInfo WriteClassMethod = typeof(TypeScriptCodeConverter)
            .GetMethod("writeClass", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("writeClass method not found");

        [Fact]
        public void StaticCacheEnabled_EmitsPrivateRegisterField() {
            var symbol = GetClassSymbol("namespace Demo { public class Foo { } }");
            var conversionClass = new ConversionClass { Name = symbol.Name, TypeSymbol = symbol };

            string output = RenderClass(null, conversionClass);

            Assert.Contains("private static readonly __type", output);
            Assert.Contains("registerType(Foo", output);
        }

        [Fact]
        public void ReflectionCanBeDisabledViaOptions() {
            var symbol = GetClassSymbol("namespace Demo { public class Foo { } }");
            var conversionClass = new ConversionClass { Name = symbol.Name, TypeSymbol = symbol };

            var options = new TypeScriptConversionOptions { Reflection = new ReflectionOptions { EnableReflection = false } };
            string output = RenderClass(options, conversionClass);

            Assert.DoesNotContain("registerType", output);
            Assert.DoesNotContain("__type", output);
        }

        [Fact]
        public void StaticCacheDisabled_EmitsTrailingRegister() {
            var symbol = GetClassSymbol("namespace Demo { public class Foo { } }");
            var conversionClass = new ConversionClass { Name = symbol.Name, TypeSymbol = symbol };

            var options = new TypeScriptConversionOptions { Reflection = new ReflectionOptions { UseStaticReflectionCache = false } };
            string output = RenderClass(options, conversionClass);

            Assert.DoesNotContain("private static readonly __type", output);
            Assert.Contains("registerType(Foo", output);
        }

        [Fact]
        public void InterfaceEmitsMetadataNamespace() {
            var symbol = GetInterfaceSymbol("public interface IFoo { }", "IFoo");
            var conversionClass = new ConversionClass {
                Name = symbol.Name,
                TypeSymbol = symbol,
                DeclarationType = MemberDeclarationType.Interface
            };

            string output = RenderClass(null, conversionClass);

            Assert.Contains("export namespace IFoo", output);
            Assert.Contains("registerMetadata", output);
        }

        [Fact]
        public void DelegateEmitsMetadataNamespace() {
            var symbol = GetDelegateSymbol("public delegate void MyDelegate(int value);", "MyDelegate");
            var conversionClass = new ConversionClass {
                Name = symbol.Name,
                TypeSymbol = symbol,
                DeclarationType = MemberDeclarationType.Delegate
            };
            conversionClass.Functions.Add(new ConversionFunction {
                Remap = symbol.Name
            });

            string output = RenderClass(null, conversionClass);

            Assert.Contains("export namespace MyDelegate", output);
            Assert.Contains("registerMetadata", output);
        }

        static INamedTypeSymbol GetClassSymbol(string code) {
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            return (INamedTypeSymbol)model.GetDeclaredSymbol(classDecl)!;
        }

        static INamedTypeSymbol GetInterfaceSymbol(string code, string name) {
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var iface = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().First(d => d.Identifier.ValueText == name);
            return (INamedTypeSymbol)model.GetDeclaredSymbol(iface)!;
        }

        static INamedTypeSymbol GetDelegateSymbol(string code, string name) {
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var del = root.DescendantNodes().OfType<DelegateDeclarationSyntax>().First(d => d.Identifier.ValueText == name);
            return (INamedTypeSymbol)model.GetDeclaredSymbol(del)!;
        }

        static string RenderClass(TypeScriptConversionOptions? options, ConversionClass conversionClass) {
            var rules = new ConversionRules();
            var original = TypeScriptReflectionEmitter.GlobalOptions.Clone();
            try {
                var converter = new TypeScriptCodeConverter(rules, TypeScriptEnvironment.NodeJS, options);
                using var ms = new MemoryStream();
                using var writer = new StreamWriter(ms, new UTF8Encoding(false), 1024, leaveOpen: true);
                WriteClassMethod.Invoke(converter, new object[] { conversionClass, writer });
                writer.Flush();
                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8);
                return reader.ReadToEnd();
            } finally {
                TypeScriptReflectionEmitter.GlobalOptions = original;
            }
        }
    }
}
