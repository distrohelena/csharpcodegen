using cs2.core;
using System.Collections.Generic;

namespace cs2.go.util {
    /// <summary>
    /// Utility helpers for Go code generation.
    /// </summary>
    public static class GoUtils {
        /// <summary>
        /// Computes inheritance info for a given class based on its extensions.
        /// </summary>
        /// <param name="program">The program containing known classes and requirements.</param>
        /// <param name="cl">The class whose inheritance metadata is inspected.</param>
        /// <param name="baseType">Outputs the resolved base class name, if any.</param>
        /// <param name="interfaces">Outputs the list of interface names.</param>
        public static void GetInheritance(GoProgram program, ConversionClass cl, out string baseType, out List<string> interfaces) {
            baseType = string.Empty;
            interfaces = new List<string>();

            for (int i = 0; i < cl.Extensions.Count; i++) {
                string ext = cl.Extensions[i];

                ConversionClass extCl = program.GetClassByName(ext);
                bool isInterface = extCl != null && extCl.DeclarationType == MemberDeclarationType.Interface;
                if (isInterface || cl.DeclarationType == MemberDeclarationType.Interface) {
                    interfaces.Add(ext);
                } else if (string.IsNullOrEmpty(baseType)) {
                    baseType = ext;
                } else {
                    interfaces.Add(ext);
                }
            }
        }
    }
}
