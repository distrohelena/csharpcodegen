using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.ts {
    public static class TypeScriptUtils {
        public static (string, string) GetInheritance(ConversionProgram program, ConversionClass cl) {
            string implements = "";
            string extends = "";

            List<string> exts = new List<string>();
            List<string> impls = new List<string>();

            for (int i = 0; i < cl.Extensions.Count; i++) {
                string ext = cl.Extensions[i];

                var extCl = program.Classes.FirstOrDefault(c => c.Name == ext);
                if (extCl == null) {
                    var knownClass = ((TypeScriptProgram)program).Requirements.FirstOrDefault(c => c.Name == ext);

                    if (knownClass == null) {
                        //throw new Exception($"Class not found: {ext}");
                    }
                }

                if (extCl?.DeclarationType == MemberDeclarationType.Interface ||
                    cl.DeclarationType == MemberDeclarationType.Interface) {
                    impls.Add(ext);
                } else {
                    exts.Add(ext);
                }
            }

            if (impls.Count > 0) {
                implements = " implements ";
                for (int i = 0; i < impls.Count; i++) {
                    if (i == impls.Count - 1) {
                        implements += $"{impls[i]}";
                    } else {
                        implements += $"{impls[i]}, ";
                    }
                }
            }

            if (exts.Count > 0) {
                extends = " extends ";
                for (int i = 0; i < exts.Count; i++) {
                    if (i == exts.Count - 1) {
                        extends += $"{exts[i]}";
                    } else {
                        extends += $"{exts[i]}, ";
                    }
                }
            }

            return (implements, extends);
        }
    }
}
