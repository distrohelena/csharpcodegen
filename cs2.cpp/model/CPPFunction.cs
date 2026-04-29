using cs2.core;

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
            List<string> lines = new List<string>();
            LayerContext context = new CPPLayerContext(program);

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

            //writer.Write("    ");
            for (int k = 0; k < lines.Count; k++) {
                string str = lines[k];
                writer.Write(str);
                if (str.IndexOf("\n") != -1 && k != lines.Count - 1) {
                    //writer.Write("    ");
                }
            }
        }
    }
}
