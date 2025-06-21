using cs2.core;

namespace cs2.ts {
    public static class TypeScriptFunction {
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
