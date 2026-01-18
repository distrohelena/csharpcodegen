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
            HashSet<string> interfaceNames = CollectInterfaceNames(cl);

            for (int i = 0; i < cl.Extensions.Count; i++) {
                string ext = cl.Extensions[i];
                bool isInterfaceType = IsInterfaceExtension(ext, interfaceNames);

                ConversionClass extCl = null;
                TypeScriptKnownClass knownClass = null;
                if (program is TypeScriptProgram tsProgram) {
                    extCl = tsProgram.GetClassByName(ext);
                    if (extCl == null) {
                        tsProgram.TryGetRequirement(ext, out knownClass);
                    }
                }

                if (!isInterfaceType) {
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

        /// <summary>
        /// Collects the simple names of interfaces implemented by the source type symbol.
        /// </summary>
        /// <param name="cl">The conversion class to inspect.</param>
        /// <returns>The interface name set, or an empty set if unavailable.</returns>
        static HashSet<string> CollectInterfaceNames(ConversionClass cl) {
            HashSet<string> interfaceNames = new HashSet<string>(StringComparer.Ordinal);
            if (cl?.TypeSymbol == null) {
                return interfaceNames;
            }

            foreach (INamedTypeSymbol iface in cl.TypeSymbol.Interfaces) {
                if (iface != null && !string.IsNullOrWhiteSpace(iface.Name)) {
                    interfaceNames.Add(iface.Name);
                }
            }

            return interfaceNames;
        }

        /// <summary>
        /// Determines whether the provided extension name represents an interface.
        /// </summary>
        /// <param name="extensionName">The raw extension name from the C# base list.</param>
        /// <param name="interfaceNames">The interface name set gathered from Roslyn symbols.</param>
        /// <returns>True if the extension is known to be an interface.</returns>
        static bool IsInterfaceExtension(string extensionName, HashSet<string> interfaceNames) {
            if (string.IsNullOrWhiteSpace(extensionName) || interfaceNames == null || interfaceNames.Count == 0) {
                return false;
            }

            string simpleName = GetSimpleTypeName(extensionName);
            if (string.IsNullOrWhiteSpace(simpleName)) {
                return false;
            }

            return interfaceNames.Contains(simpleName);
        }

        /// <summary>
        /// Extracts the simple type name from a potentially qualified or generic type string.
        /// </summary>
        /// <param name="typeName">The raw type name.</param>
        /// <returns>The simple name without namespace or generic suffixes.</returns>
        static string GetSimpleTypeName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return string.Empty;
            }

            string trimmed = typeName.Trim();
            if (trimmed.StartsWith("global::", StringComparison.Ordinal)) {
                trimmed = trimmed.Substring("global::".Length);
            }

            int genericIndex = trimmed.IndexOf('<');
            if (genericIndex >= 0) {
                trimmed = trimmed.Substring(0, genericIndex);
            }

            int arrayIndex = trimmed.IndexOf('[');
            if (arrayIndex >= 0) {
                trimmed = trimmed.Substring(0, arrayIndex);
            }

            int lastDot = trimmed.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < trimmed.Length - 1) {
                trimmed = trimmed.Substring(lastDot + 1);
            }

            return trimmed;
        }
    }
}
