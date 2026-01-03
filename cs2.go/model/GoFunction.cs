using cs2.core;
using cs2.go.util;
using System.IO;
using System.Collections.Generic;

namespace cs2.go {
    /// <summary>
    /// Helper methods for emitting Go function bodies from conversion metadata.
    /// </summary>
    public static class GoFunction {
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
            GoConversiorProcessor conversion,
            GoProgram program,
            ConversionClass cl,
            StreamWriter writer = null) {
            List<string> lines = new List<string>();
            LayerContext context = new GoLayerContext(program);

            int start = context.DepthClass;
            int startFn = context.DepthFunction;

            context.AddClass(cl);
            context.AddFunction(new FunctionStack(fn));

            if (fn.ArrowExpression != null) {
                conversion.ProcessArrowExpressionClause(cl.Semantic, context, fn.ArrowExpression, lines);
            } else if (fn.RawBlock != null) {
                conversion.ProcessBlock(cl.Semantic, context, fn.RawBlock, lines);
            }

            context.PopClass(start);
            context.PopFunction(startFn);

            if (writer != null) {
                PrintLines(new GoOutputWriter(writer), lines);
            }

            return lines;
        }

        /// <summary>
        /// Writes already-rendered lines to the output stream.
        /// </summary>
        /// <param name="writer">The writer that receives output.</param>
        /// <param name="lines">The lines to emit.</param>
        public static void PrintLines(GoOutputWriter writer, List<string> lines) {
            if (writer == null) {
                return;
            }

            writer.WriteLines(lines, string.Empty);
        }

        /// <summary>
        /// Writes already-rendered lines to the output stream.
        /// </summary>
        /// <param name="writer">The writer that receives output.</param>
        /// <param name="lines">The lines to emit.</param>
        public static void PrintLines(StreamWriter writer, List<string> lines) {
            if (writer == null) {
                return;
            }

            PrintLines(new GoOutputWriter(writer), lines);
        }
    }
}
