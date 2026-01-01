using cs2.core;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>
    /// Expression-level tests for TypeScriptConversiorProcessor. Each test asserts on the emitted
    /// TypeScript snippet and also validates syntax via the TypeScript compiler when available.
    /// </summary>
    public class TypeScriptConversiorProcessorTests_Expressions {
        [Fact]
        public void Assignment_Basic_EmitsEquals() {
            var code = "class C { int a; int b; void M(){ a = b; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetMethodByName(root, "M");
            var stmt = RoslynTestHelper.GetSingleStatement(method);

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, stmt);

            var output = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains(" = ", output);
        }

        [Fact]
        public void IdentifierName_Simple_VariableReferenced() {
            var code = "class C { int a; void M(){ a; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var expr = method.DescendantNodes().OfType<IdentifierNameSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            var output = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains("a", output);
        }

        [Fact]
        public void ObjectCreation_WithOverloads_ResolvesConstructorIndexOrNew() {
            var code = "class C { void M(){ var x = new MyClass(1); } } class MyClass { }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var creation = method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().First();

            var (proc, ctx, prog) = TsProcessorTestHarness.Create();
            // Prepare class with a constructor
            var my = new ConversionClass { Name = "MyClass" };
            var ctor1 = new ConversionFunction { Name = ".ctor", IsConstructor = true };
            ctor1.InParameters.Add(new ConversionVariable { Name = "a", VarType = VariableUtil.GetVarType("Int32") });
            my.Functions.AddRange(new[] { ctor1 });
            prog.Classes.Add(my);

            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, creation);
            var output = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains("new ", output);
        }

        [Fact]
        public void MemberAccess_EmitsDot() {
            var code = "class C { string s; void M(){ s.Length; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var member = method.DescendantNodes().OfType<MemberAccessExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, member);
            var output = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains('.', output);
        }

        [Fact]
        public void Invocation_WithOutParameter_GeneratesTempAndAfterAssignment() {
            var code = @"class C { static void Foo(out int x){ x = 1; } void M(){ int x; Foo(out x); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetMethodByName(root, "M");

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var (lines, result) = TsProcessorTestHarness.RunProcessBlock(proc, ctx, model, method.Body!);
            // Expect temporary out var and afterLines assignment back
            Assert.NotNull(result.BeforeLines);
            Assert.Contains("let ", string.Concat(result.BeforeLines));
            Assert.NotNull(result.AfterLines);
            var after = string.Concat(result.AfterLines);
            Assert.Contains("x = out_", after);
            Assert.Contains(".value;", after);
        }

        [Fact]
        public void ThisExpression_EmitsThis() {
            var code = "class C { void M(){ this; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var expr = method.DescendantNodes().OfType<ThisExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            Assert.Contains("this", TsProcessorTestHarness.JoinLines(lines));
        }

        [Fact]
        public void BinaryExpression_Addition_EmitsOperator() {
            var code = "class C { int a; int b; void M(){ a + b; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var expr = method.DescendantNodes().OfType<BinaryExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            Assert.Contains(" + ", TsProcessorTestHarness.JoinLines(lines));
        }

        /// <summary>
        /// Ensures switch expressions are emitted as IIFE-based expressions.
        /// </summary>
        [Fact]
        public void SwitchExpression_EmitsIife() {
            var code = "enum LoggingEventType { Log, Warning } class C { string M(LoggingEventType type){ return type switch { LoggingEventType.Log => \"LOG\", _ => \"WARN\" }; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var expr = root.DescendantNodes().OfType<SwitchExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx, returnType: new VariableType(VariableDataType.String));
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            var output = TsProcessorTestHarness.JoinLines(lines);

            Assert.Contains("const __switch", output);
            Assert.Contains("return ", output);
        }

        /// <summary>
        /// Ensures range element access uses slice syntax.
        /// </summary>
        [Fact]
        public void RangeElementAccess_EmitsSlice() {
            var code = "class C { string M(string s){ return s[..3]; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var expr = root.DescendantNodes().OfType<ElementAccessExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx, returnType: new VariableType(VariableDataType.String));
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            var output = TsProcessorTestHarness.JoinLines(lines);

            Assert.Contains(".slice(", output);
        }

        [Fact]
        public void ImplicitArrayCreation_EmitsBrackets() {
            var code = "class C { void M(){ var x = new[] {1,2,3}; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var expr = method.DescendantNodes().OfType<ImplicitArrayCreationExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            var output = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains("[", output);
            Assert.Contains("]", output);
        }

        [Fact]
        public void AwaitExpression_MarksAwait() {
            var code = "using System.Threading.Tasks; class C { async Task M(){ await Task.FromResult(1); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var expr = method.DescendantNodes().OfType<AwaitExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx, returnType: new VariableType(VariableDataType.Void));
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            Assert.StartsWith("await ", TsProcessorTestHarness.JoinLines(lines));
        }

        [Fact]
        public void QualifiedName_InTypeOf_IsHandled() {
            var code = "class C { void M(){ typeof(System.Int32); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var expr = method.DescendantNodes().OfType<TypeOfExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            var s = TsProcessorTestHarness.JoinLines(lines);
            Assert.StartsWith("typeof ", s);
            Assert.Contains("Int32", s);
        }

        [Fact]
        public void TypeOfExpression_EmitsTypeof() {
            var code = "class C { void M(){ typeof(int); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var expr = method.DescendantNodes().OfType<TypeOfExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, expr);
            Assert.StartsWith("typeof ", TsProcessorTestHarness.JoinLines(lines));
        }

        [Fact]
        public void SimpleLambda_EmitsArrow() {
            var code = "using System; class C { void M(){ Func<int,int> f = x => x + 1; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var lambda = method.DescendantNodes().OfType<SimpleLambdaExpressionSyntax>().First();

            var (proc, ctx, prog) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            // minimal support for int type mapping owner class
            prog.Classes.Add(new ConversionClass { Name = "Int32" });
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, lambda);
            Assert.Contains(" => ", TsProcessorTestHarness.JoinLines(lines));
        }

        [Fact]
        public void ArrayCreation_EmitsNewArray() {
            var code = "class C { void M(){ var x = new int[3]; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var arrayCreation = method.DescendantNodes().OfType<ArrayCreationExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, arrayCreation);
            Assert.Contains("new Array(", TsProcessorTestHarness.JoinLines(lines));
        }

        [Fact]
        public void ParenthesizedExpression_EmitsParens() {
            var code = "class C { int a; int b; void M(){ (a + b); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var paren = method.DescendantNodes().OfType<ParenthesizedExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, paren);
            var joined = TsProcessorTestHarness.JoinLines(lines);
            Assert.StartsWith("(", joined);
            Assert.EndsWith(")", joined);
        }

        [Fact]
        public void BaseExpression_EmitsSuper() {
            var code = "class B { } class C:B { void M(){ base.ToString(); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var baseExpr = method.DescendantNodes().OfType<BaseExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, baseExpr);
            Assert.Contains("super", TsProcessorTestHarness.JoinLines(lines));
        }

        [Fact]
        public void InitializerExpression_ObjectAndArray_EmitsBraces() {
            var code = "class C { int A {get;set;} void M(){ new C{ A = 1 }; new int[]{1,2}; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var inits = method.DescendantNodes().OfType<InitializerExpressionSyntax>();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            foreach (var init in inits) {
                var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, init);
                var s = TsProcessorTestHarness.JoinLines(lines);
                Assert.True(s.Contains("{") || s.Contains("["));
            }
        }

        [Fact]
        public void TupleExpression_EmitsTsTuple() {
            var code = "class C { void M(){ var t = (1,2); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var tuple = method.DescendantNodes().OfType<TupleExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, tuple);
            var s = TsProcessorTestHarness.JoinLines(lines);
            Assert.StartsWith("[", s);
            Assert.EndsWith("]", s);
        }

        [Fact]
        public void DefaultExpression_MapsTypes() {
            var code = "class C { void M(){ default(int); default(bool); default(string); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var defaults = method.DescendantNodes().OfType<DefaultExpressionSyntax>();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            foreach (var def in defaults) {
                var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, def);
                var s = TsProcessorTestHarness.JoinLines(lines);
                Assert.True(s == "0" || s == "false" || s == "null" || s == "'\\0'");
            }
        }

        [Fact]
        public void InterpolatedString_UsesBackticks() {
            var code = "class C { int v=1; void M(){ $\"val:{v}\"; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var interp = method.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, interp);
            var s = TsProcessorTestHarness.JoinLines(lines);
            Assert.StartsWith("`", s);
            Assert.EndsWith("`", s);
        }

        [Fact]
        public void ElementAccess_EmitsBracketAccess() {
            var code = "class C { int[] arr = new int[1]; void M(){ arr[0]; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var el = method.DescendantNodes().OfType<ElementAccessExpressionSyntax>().First();

            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, el);
            var s = TsProcessorTestHarness.JoinLines(lines);
            Assert.Contains("[", s);
            Assert.Contains("]", s);
        }

        [Fact]
        public void PostfixUnary_EmitsOperator() {
            var code = "class C { int i; void M(){ i++; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var post = method.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = TsProcessorTestHarness.JoinLines(TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, post));
            Assert.EndsWith("++", s);
        }

        [Fact]
        public void PrefixUnary_EmitsOperator() {
            var code = "class C { int i; void M(){ ++i; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var pre = method.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = TsProcessorTestHarness.JoinLines(TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, pre));
            Assert.StartsWith("+", s); // could be ++ or + depending on token
        }

        [Fact]
        public void ConditionalAccess_EmitsQuestionDot() {
            var code = "class C { string s; void M(){ s?.ToString(); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var cond = method.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = TsProcessorTestHarness.JoinLines(TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, cond));
            Assert.Contains("?.", s);
        }

        [Fact]
        public void CastExpression_EmitsParensOrRemap() {
            var code = "class C { object o; void M(){ (int)o; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var cast = method.DescendantNodes().OfType<CastExpressionSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = TsProcessorTestHarness.JoinLines(TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, cast));
            Assert.Contains("Int32", s);
        }

        [Fact]
        public void ConditionalExpression_Ternary_EmitsQMarkColon() {
            var code = "class C { bool b; int x; int y; void M(){ var _ = b ? x : y; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var tern = method.DescendantNodes().OfType<ConditionalExpressionSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = TsProcessorTestHarness.JoinLines(TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, tern));
            Assert.Contains("?", s);
            Assert.Contains(":", s);
        }

        [Fact]
        public void ParenthesizedLambda_EmitsArrow() {
            var code = "using System; class C { void M(){ Func<int,int,int> f = (x,y) => x + y; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var lambda = method.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>().First();
            var (proc, ctx, prog) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            prog.Classes.Add(new ConversionClass { Name = "Int32" });
            var s = TsProcessorTestHarness.JoinLines(TsProcessorTestHarness.RunProcessExpression(proc, ctx, model, lambda));
            Assert.Contains(" => ", s);
        }

        [Fact]
        public void DeclarationExpression_InOutVar_IsNotImplemented_Yet() {
            var code = @"class C { static void Foo(out int x){ x=1; } void M(){ Foo(out var z); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var invoke = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First(i => i.ToString().StartsWith("Foo("));
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            Assert.Throws<NotImplementedException>(() => {
                var lines = new List<string>();
                proc.ProcessExpression(model, ctx, invoke, lines);
            });
        }
    }
}
