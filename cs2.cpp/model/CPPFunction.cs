using cs2.core;

using Microsoft.CodeAnalysis;

namespace cs2.cpp {
    public static class CPPFunction {
        /// <summary>
        /// Emits the converted function body lines into the supplied text writer.
        /// </summary>
        /// <param name="fn">The function whose body should be lowered.</param>
        /// <param name="conversion">Processor used to lower Roslyn syntax into C++ source tokens.</param>
        /// <param name="program">Program model used by the lowering context.</param>
        /// <param name="cl">Owning class for the function body.</param>
        /// <param name="writer">Writer that receives the lowered body.</param>
        public static void WriteLines(this ConversionFunction fn, ConversionProcessor conversion, ConversionProgram program, ConversionClass cl, TextWriter writer) {
            if (TryWriteNativeOwnershipHelperLines(fn, cl, writer)) {
                return;
            }

            List<string> lines = new List<string>();
            LayerContext context = new CPPLayerContext(program);

            int start = context.DepthClass;
            int startFn = context.DepthFunction;

            context.AddClass(cl);
            context.AddFunction(new FunctionStack(fn));
            SemanticModel semantic = fn.Semantic ?? cl.Semantic;

            if (fn.ArrowExpression != null) {
                WriteExpressionBodiedFunctionLines(fn, conversion, cl, context, lines);
            } else if (fn.RawBlock != null) {
                conversion.ProcessBlock(semantic, context, fn.RawBlock, lines);
            }

            context.PopClass(start);
            context.PopFunction(startFn);

            //writer.Write("    ");
            for (int k = 0; k < lines.Count; k++) {
                string str = lines[k];
                writer.Write(str);
                if (str.IndexOf("\n") != -1 && k != lines.Count - 1) {
                    //writer.Write("    ");
                }
            }
        }

        /// <summary>
        /// Emits the canonical native ownership helper bodies when converting the helper type itself.
        /// </summary>
        /// <param name="fn">Function currently being emitted.</param>
        /// <param name="cl">Owning class for the function.</param>
        /// <param name="writer">Writer receiving the emitted body.</param>
        /// <returns>True when the function matched one supported NativeOwnership helper and was emitted directly.</returns>
        static bool TryWriteNativeOwnershipHelperLines(ConversionFunction fn, ConversionClass cl, TextWriter writer) {
            if (fn == null || cl == null || writer == null) {
                return false;
            }

            if (!string.Equals(cl.Name, "NativeOwnership", StringComparison.Ordinal) || fn.InParameters.Count != 1) {
                return false;
            }

            string parameterName = fn.InParameters[0].Name;
            if (string.IsNullOrWhiteSpace(parameterName)) {
                parameterName = "value";
            }

            if (string.Equals(fn.Name, "Delete", StringComparison.Ordinal)) {
                writer.WriteLine($"    if ({parameterName} != nullptr)");
                writer.WriteLine("    {");
                writer.WriteLine($"        delete {parameterName};");
                writer.WriteLine("    }");
                return true;
            }

            if (string.Equals(fn.Name, "DisposeAndDelete", StringComparison.Ordinal)) {
                writer.WriteLine($"    if ({parameterName} != nullptr)");
                writer.WriteLine("    {");
                writer.WriteLine($"        {parameterName}->Dispose();");
                writer.WriteLine($"        delete {parameterName};");
                writer.WriteLine("    }");
                return true;
            }

            if (string.Equals(fn.Name, "Release", StringComparison.Ordinal)) {
                writer.WriteLine($"    if ({parameterName} != nullptr)");
                writer.WriteLine("    {");
                writer.WriteLine($"        delete {parameterName};");
                writer.WriteLine("    }");
                writer.WriteLine($"    {parameterName} = nullptr;");
                return true;
            }

            if (string.Equals(fn.Name, "DisposeAndRelease", StringComparison.Ordinal)) {
                writer.WriteLine($"    if ({parameterName} != nullptr)");
                writer.WriteLine("    {");
                writer.WriteLine($"        {parameterName}->Dispose();");
                writer.WriteLine($"        delete {parameterName};");
                writer.WriteLine("    }");
                writer.WriteLine($"    {parameterName} = nullptr;");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Emits an expression-bodied function using statement semantics so method bodies do not inherit field-initializer lowering.
        /// </summary>
        /// <param name="fn">The function whose arrow expression should be emitted.</param>
        /// <param name="conversion">Processor used to lower the Roslyn expression.</param>
        /// <param name="cl">Owning class for semantic binding.</param>
        /// <param name="context">Current lowering context.</param>
        /// <param name="lines">Destination collection that receives the lowered body tokens.</param>
        static void WriteExpressionBodiedFunctionLines(
            ConversionFunction fn,
            ConversionProcessor conversion,
            ConversionClass cl,
            LayerContext context,
            List<string> lines) {
            if (fn == null) {
                throw new ArgumentNullException(nameof(fn));
            }

            if (conversion == null) {
                throw new ArgumentNullException(nameof(conversion));
            }

            if (cl == null) {
                throw new ArgumentNullException(nameof(cl));
            }

            if (context == null) {
                throw new ArgumentNullException(nameof(context));
            }

            if (lines == null) {
                throw new ArgumentNullException(nameof(lines));
            }

            if (fn.ArrowExpression == null) {
                throw new ArgumentException("Expression-bodied lowering requires an arrow expression.", nameof(fn));
            }

            List<string> expressionLines = new List<string>();
            SemanticModel semantic = fn.Semantic ?? cl.Semantic;
            ExpressionResult expressionResult = conversion.ProcessExpression(semantic, context, fn.ArrowExpression.Expression, expressionLines);

            if (expressionResult.BeforeLines != null && expressionResult.BeforeLines.Count > 0) {
                lines.AddRange(expressionResult.BeforeLines);
            }

            if (ReturnsVoid(fn)) {
                if (expressionLines.Count > 0) {
                    lines.AddRange(expressionLines);
                    lines.Add(";");
                }

                if (expressionResult.AfterLines != null && expressionResult.AfterLines.Count > 0) {
                    lines.AddRange(expressionResult.AfterLines);
                }

                return;
            }

            if (expressionResult.AfterLines == null || expressionResult.AfterLines.Count == 0) {
                lines.Add("return ");
                lines.AddRange(expressionLines);
                lines.Add(";");
                return;
            }

            lines.Add("auto ___result = ");
            lines.AddRange(expressionLines);
            lines.Add(";\n");
            lines.AddRange(expressionResult.AfterLines);
            lines.Add("return ___result;");
        }

        /// <summary>
        /// Determines whether the converted function should emit a statement-only body.
        /// </summary>
        /// <param name="fn">The function metadata to inspect.</param>
        /// <returns><c>true</c> when the function returns void; otherwise <c>false</c>.</returns>
        static bool ReturnsVoid(ConversionFunction fn) {
            return fn.ReturnType == null || fn.ReturnType.Type == VariableDataType.Void;
        }
    }
}
