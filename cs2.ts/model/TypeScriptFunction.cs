using cs2.core;

namespace cs2.ts {
    /// <summary>
    /// Helper methods for emitting TypeScript function bodies from conversion metadata.
    /// </summary>
    public static class TypeScriptFunction {
        /// <summary>
        /// Renders a function body into a list of lines, optionally writing to a stream.
        /// </summary>
        /// <param name="fn">The conversion function to render.</param>
        /// <param name="conversion">The conversion processor that renders syntax nodes.</param>
        /// <param name="program">The program context used for type mapping.</param>
        /// <param name="cl">The class that owns the function.</param>
        /// <param name="writer">Optional writer that receives the output immediately.</param>
        /// <returns>The rendered lines for the function body.</returns>
        public static List<string> WriteLines(
            this ConversionFunction fn, 
            ConversionProcessor conversion, 
            ConversionProgram program, 
            ConversionClass cl, 
            StreamWriter writer = null) {
            List<string> lines = new List<string>();
            LayerContext context = new TypeScriptLayerContext((TypeScriptProgram)program);

            int start = context.DepthClass;
            int startFn = context.DepthFunction;

            context.AddClass(cl);
            context.AddFunction(new FunctionStack(fn));

            lines.Add("        ");
            if (fn.ArrowExpression != null) {
                conversion.ProcessArrowExpressionClause(cl.Semantic, context, fn.ArrowExpression, lines);
            } else if (fn.RawBlock != null) {
                conversion.ProcessBlock(cl.Semantic, context, fn.RawBlock, lines);
            }

            context.PopClass(start);
            context.PopFunction(startFn);

            if (writer != null) {
                PrintLines(writer, lines);
            }

            return lines;
        }

        /// <summary>
        /// Writes already-rendered lines to the output stream with indentation preservation.
        /// </summary>
        /// <param name="writer">The writer that receives output.</param>
        /// <param name="lines">The lines to emit.</param>
        public static void PrintLines(StreamWriter writer, List<string> lines) {
            for (int k = 0; k < lines.Count; k++) {
                string str = lines[k];
                writer.Write(str);
                if (str.IndexOf("\n") != -1 && k != lines.Count - 1) {
                    writer.Write("        ");
                }
            }
        }
    }
}
