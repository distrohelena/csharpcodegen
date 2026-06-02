using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp {
    public static class CPPUtils {
        public static string GetInheritance(ConversionProgram program, ConversionClass cl) {
            List<string> exts = new List<string>();

            if (cl.IsValueType) {
                return string.Empty;
            }

            for (int i = 0; i < cl.Extensions.Count; i++) {
                string ext = cl.Extensions[i];
                if (string.IsNullOrWhiteSpace(ext)) {
                    continue;
                }

                ConversionClass? generatedBaseClass = program.FindGeneratedClass(ext, 0);
                string emittedBaseTypeName = generatedBaseClass?.GetEmittedTypeName() ?? ext;
                exts.Add($"public {emittedBaseTypeName}");
            }

            return string.Join(", ", exts);
        }
    }
}
