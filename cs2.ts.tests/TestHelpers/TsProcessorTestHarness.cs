using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using cs2.core;
using Microsoft.CodeAnalysis.CSharp;

namespace cs2.ts.tests.TestHelpers {
    /// <summary>
    /// Centralizes processor + context setup and runs conversions while validating
    /// generated TypeScript snippets for syntax and basic type correctness.
    /// </summary>
    internal static class TsProcessorTestHarness {
        public static (TypeScriptConversiorProcessor Processor, TypeScriptLayerContext Context, TypeScriptProgram Program) Create() {
            var rules = new ConversionRules();
            var program = new TypeScriptProgram(rules);
            var context = new TypeScriptLayerContext(program);
            var processor = new TypeScriptConversiorProcessor();
            return (processor, context, program);
        }

        public static void PushClassAndFunction(TypeScriptLayerContext context, string className = "C", string functionName = "M", VariableType? returnType = null) {
            var cl = new ConversionClass { Name = className };
            context.Program.Classes.Add(cl);
            context.AddClass(cl);

            var fn = new ConversionFunction {
                Name = functionName,
                ReturnType = returnType ?? new VariableType(VariableDataType.Void)
            };
            context.AddFunction(new FunctionStack(fn));
        }

        /// <summary>
        /// Processes a statement, returns resulting TS lines, and validates them via ts compiler.
        /// </summary>
        public static List<string> RunProcessStatement(TypeScriptConversiorProcessor proc, TypeScriptLayerContext context, SemanticModel model, StatementSyntax stmt) {
            var lines = new List<string>();
            if (stmt.Parent is BlockSyntax block) {
                proc.ProcessBlock(model, context, block, lines, depth: 0);
            } else {
                // Fallback: wrap in a new block (may lose semantic context for type queries)
                proc.ProcessBlock(model, context, SyntaxFactory.Block(stmt), lines, depth: 0);
            }
            TsTypeChecker.AssertValidTypeScript(lines);
            return lines;
        }

        /// <summary>
        /// Processes an expression, returns resulting TS lines, and validates them via ts compiler.
        /// </summary>
        public static List<string> RunProcessExpression(TypeScriptConversiorProcessor proc, TypeScriptLayerContext context, SemanticModel model, ExpressionSyntax expr) {
            var lines = new List<string>();
            proc.ProcessExpression(model, context, expr, lines);
            TsTypeChecker.AssertValidTypeScript(lines);
            return lines;
        }

        public static string JoinLines(IEnumerable<string> lines) => string.Concat(lines);

        /// <summary>
        /// Processes a block, returns both TS lines and the last ExpressionResult (for assertions on Before/After lines).
        /// </summary>
        public static (List<string> Lines, cs2.core.ExpressionResult Result) RunProcessBlock(TypeScriptConversiorProcessor proc, TypeScriptLayerContext context, SemanticModel model, BlockSyntax block) {
            var lines = new List<string>();
            var result = proc.ProcessBlock(model, context, block, lines, depth: 0);
            return (lines, result);
        }
    }
}
