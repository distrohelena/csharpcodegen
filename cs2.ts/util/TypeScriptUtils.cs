using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.ts {
    /// <summary>
    /// Utility helpers for TypeScript code generation.
    /// </summary>
    public static class TypeScriptUtils {
        /// <summary>
        /// Computes implements/extends clauses for a given class based on its extensions and declaration type.
        /// </summary>
        /// <param name="program">The program containing known classes and requirements.</param>
        /// <param name="cl">The class whose inheritance metadata is inspected.</param>
        /// <param name="implements">Outputs the formatted implements clause, if any.</param>
        /// <param name="extends">Outputs the formatted extends clause, if any.</param>
        public static void GetInheritance(ConversionProgram program, ConversionClass cl, out string implements, out string extends) {
            implements = "";
            extends = "";

            List<string> exts = new List<string>();
            List<string> impls = new List<string>();

            bool isInterfaceDeclaration = cl.DeclarationType == MemberDeclarationType.Interface;

            for (int i = 0; i < cl.Extensions.Count; i++) {
                string ext = cl.Extensions[i];

                ConversionClass extCl = null;
                TypeScriptKnownClass knownClass = null;
                if (program is TypeScriptProgram tsProgram) {
                    extCl = tsProgram.GetClassByName(ext);
                    if (extCl == null) {
                        tsProgram.TryGetRequirement(ext, out knownClass);
                    }
                }

                bool isInterfaceType = false;
                if (extCl != null) {
                    isInterfaceType = extCl.DeclarationType == MemberDeclarationType.Interface;
                } else if (knownClass != null && knownClass.Symbols != null) {
                    for (int j = 0; j < knownClass.Symbols.Count; j++) {
                        if (knownClass.Symbols[j] != null && knownClass.Symbols[j].Type == "interface") {
                            isInterfaceType = true;
                            break;
                        }
                    }
                }

                if (isInterfaceDeclaration) {
                    exts.Add(ext);
                } else if (isInterfaceType) {
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

        }
    }
}
