using System.Linq;
using cs2.ts.tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>
    /// Statement-level tests for control flow, declarations, and other constructs.
    /// Ensures TS emission compiles and preserves intent.
    /// </summary>
    public class TypeScriptConversiorProcessorTests_Statements {
        [Fact]
        public void ReturnStatement_EmitsReturn() {
            var code = "class C { int X(){ return 1; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var ret = method.DescendantNodes().OfType<ReturnStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx, returnType: new cs2.core.VariableType(cs2.core.VariableDataType.Int32));
            var lines = TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, ret);
            Assert.Contains("return", string.Concat(lines));
        }

        [Fact]
        public void EmptyStatement_EmitsSemicolon() {
            var code = "class C { void M(){ ; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var empty = method.DescendantNodes().OfType<EmptyStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, empty);
            Assert.Contains(";", string.Concat(lines));
        }

        [Fact]
        public void DoWhile_EmitsDoWhile() {
            var code = "class C { void M(){ int i=0; do { i++; } while (i < 1); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var doStmt = method.DescendantNodes().OfType<DoStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var lines = TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, doStmt);
            var s = string.Concat(lines);
            Assert.Contains("do {", s);
            Assert.Contains(")", s);
        }

        [Fact]
        public void UsingStatement_EmitsTryFinally() {
            var code = "using System; class D: IDisposable { public void Dispose(){} } class C { void M(){ using (var d = new D()) { } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetMethodByName(root, "M");
            var usingStmt = method.DescendantNodes().OfType<UsingStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, usingStmt));
            Assert.Contains("try {", s);
            Assert.Contains("finally", s);
        }

        [Fact]
        public void LockStatement_AddsComment() {
            var code = "class C { object o=new(); void M(){ lock(o) { } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var lockStmt = method.DescendantNodes().OfType<LockStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, lockStmt));
            Assert.Contains("Lock omitted in TypeScript", s);
        }

        [Fact]
        public void TryCatchFinally_EmitsAllBlocks() {
            var code = "class C { void M(){ try { } catch(System.Exception e) { } finally { } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var tryStmt = method.DescendantNodes().OfType<TryStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, tryStmt));
            Assert.Contains("try {", s);
            Assert.Contains("catch (", s);
            Assert.Contains("finally {", s);
        }

        [Fact]
        public void ForEach_EmitsForOf() {
            var code = "using System.Collections.Generic; class C { void M(){ foreach(var x in new List<int>()) { } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var fe = method.DescendantNodes().OfType<ForEachStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, fe));
            Assert.StartsWith("for (let ", s);
            Assert.Contains(" of ", s);
        }

        [Fact]
        public void Continue_EmitsContinue() {
            var code = "class C { void M(){ for(int i=0;i<1;i++){ continue; } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var cont = method.DescendantNodes().OfType<ContinueStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, cont));
            Assert.Contains("continue;", s);
        }

        [Fact]
        public void While_EmitsWhile() {
            var code = "class C { void M(){ int i=0; while(i<1){ i++; } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var wh = method.DescendantNodes().OfType<WhileStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, wh));
            Assert.Contains("while (", s);
        }

        [Fact]
        public void For_EmitsFor() {
            var code = "class C { void M(){ for(int i=0;i<1;i++){ } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var f = method.DescendantNodes().OfType<ForStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, f));
            Assert.StartsWith("for (", s);
        }

        [Fact]
        public void IfElse_EmitsIfElse() {
            var code = "class C { void M(){ if (true) { } else { } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var ifs = method.DescendantNodes().OfType<IfStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, ifs));
            Assert.StartsWith("if (", s);
            Assert.Contains("else", s);
        }

        [Fact]
        public void Throw_EmitsThrow() {
            var code = "class C { void M(){ throw new System.Exception(); } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var thr = method.DescendantNodes().OfType<ThrowStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, thr));
            Assert.Contains("throw ", s);
        }

        [Fact]
        public void Switch_EmitsSwitch() {
            var code = "class C { void M(){ switch(1){ case 1: break; default: break; } } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var sw = method.DescendantNodes().OfType<SwitchStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, sw));
            Assert.StartsWith("switch (", s);
            Assert.Contains("case ", s);
            Assert.Contains("default:", s);
        }

        [Fact]
        public void VariableDeclaration_EmitsLet() {
            var code = "class C { void M(){ int x = 1; } }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var method = RoslynTestHelper.GetFirstMethod(root);
            var decl = method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx);
            var s = string.Concat(TsProcessorTestHarness.RunProcessStatement(proc, ctx, model, decl));
            Assert.StartsWith("let ", s);
        }

        [Fact]
        public void ArrowExpressionClause_EmitsReturn() {
            var code = "class C { int P => 1; }";
            var (_, model, root) = RoslynTestHelper.CreateCompilation(code);
            var arrow = root.DescendantNodes().OfType<ArrowExpressionClauseSyntax>().First();
            var (proc, ctx, _) = TsProcessorTestHarness.Create();
            TsProcessorTestHarness.PushClassAndFunction(ctx, returnType: new cs2.core.VariableType(cs2.core.VariableDataType.Int32));
            var lines = new System.Collections.Generic.List<string>();
            proc.ProcessArrowExpressionClause(model, ctx, arrow, lines);
            Assert.Contains("return ", string.Concat(lines));
        }
    }
}
