using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp {
    public static class CPPUtils {
        public static string GetInheritance(ConversionProgram program, ConversionClass cl) {
            List<string> exts = new List<string>();

            for (int i = 0; i < cl.Extensions.Count; i++) {
                string ext = cl.Extensions[i];
                if (string.IsNullOrWhiteSpace(ext)) {
                    continue;
                }

                exts.Add($"public {ext}");
            }

            return string.Join(", ", exts);
        }
    }
}
