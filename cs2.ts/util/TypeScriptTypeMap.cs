using cs2.core;
using System;
using System.Collections.Generic;

namespace cs2.ts.util {
    /// <summary>
    /// Centralizes C# to TypeScript primitive type mappings for the TypeScript backend.
    /// </summary>
    public static class TypeScriptTypeMap {
        /// <summary>
        /// Gets the primitive mapping table used by the converter.
        /// </summary>
        public static IReadOnlyDictionary<string, string> PrimitiveMappings { get; } = new Dictionary<string, string> {
            { "object", "any" },

            { "Byte", "number" },
            { "byte", "number" },
            { "sbyte", "number" },
            { "short", "number" },
            { "ushort", "number" },
            { "int", "number" },
            { "Int16", "number" },
            { "Int32", "number" },
            { "Int64", "number" },
            { "uint", "number" },
            { "UInt16", "number" },
            { "UInt32", "number" },
            { "UInt64", "bigint" },
            { "long", "number" },
            { "ulong", "bigint" },
            { "float", "number" },
            { "double", "number" },
            { "decimal", "number" },
            { "Decimal", "number" },
            { "Single", "number" },

            { "bool", "boolean" },
            { "Boolean", "boolean" },

            { "char", "string" },
            { "string", "string" },
            { "String", "string" },

            { "Array", "Array<any>" }
        };

        /// <summary>
        /// Populates the program type map with known primitive mappings.
        /// </summary>
        /// <param name="program">The conversion program to update.</param>
        public static void PopulateTypeMap(ConversionProgram program) {
            if (program == null) {
                throw new ArgumentNullException(nameof(program));
            }

            foreach (var pair in PrimitiveMappings) {
                if (!program.TypeMap.ContainsKey(pair.Key)) {
                    program.TypeMap.Add(pair.Key, pair.Value);
                }
            }
        }

        /// <summary>
        /// Resolves a primitive C# type name to its TypeScript equivalent.
        /// </summary>
        /// <param name="typeName">The C# type name or keyword.</param>
        /// <returns>The mapped TypeScript type name, or the original name when unmapped.</returns>
        public static string GetTypeScriptTypeName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return typeName;
            }

            if (PrimitiveMappings.TryGetValue(typeName, out string mapped)) {
                return mapped;
            }

            return typeName;
        }

        /// <summary>
        /// Resolves a primitive C# type name to its boxed TypeScript equivalent.
        /// </summary>
        /// <param name="typeName">The C# type name or keyword.</param>
        /// <returns>The boxed TypeScript type name, or the primitive when unmapped.</returns>
        public static string GetTypeScriptBoxedTypeName(string typeName) {
            string primitive = GetTypeScriptTypeName(typeName);
            return primitive switch {
                "number" => "Number",
                "boolean" => "Boolean",
                "string" => "String",
                _ => primitive
            };
        }

        /// <summary>
        /// Attempts to resolve a primitive C# type name to its TypeScript equivalent.
        /// </summary>
        /// <param name="typeName">The C# type name or keyword.</param>
        /// <param name="mapped">The mapped TypeScript type name.</param>
        /// <returns>True when the type was mapped.</returns>
        public static bool TryGetTypeScriptTypeName(string typeName, out string mapped) {
            if (!string.IsNullOrWhiteSpace(typeName) && PrimitiveMappings.TryGetValue(typeName, out mapped)) {
                return true;
            }

            mapped = string.Empty;
            return false;
        }
    }
}
