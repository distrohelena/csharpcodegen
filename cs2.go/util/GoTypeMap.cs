using cs2.core;
using System;
using System.Collections.Generic;

namespace cs2.go.util {
    /// <summary>
    /// Centralizes C# to Go primitive type mappings for the Go backend.
    /// </summary>
    public static class GoTypeMap {
        /// <summary>
        /// Gets the primitive mapping table used by the converter.
        /// </summary>
        public static IReadOnlyDictionary<string, GoTypeMapEntry> PrimitiveMappings { get; } = new Dictionary<string, GoTypeMapEntry> {
            { "object", new GoTypeMapEntry("interface{}") },

            { "Byte", new GoTypeMapEntry("byte") },
            { "byte", new GoTypeMapEntry("byte") },
            { "sbyte", new GoTypeMapEntry("int8") },
            { "short", new GoTypeMapEntry("int16") },
            { "ushort", new GoTypeMapEntry("uint16") },
            { "int", new GoTypeMapEntry("int") },
            { "Int16", new GoTypeMapEntry("int16") },
            { "Int32", new GoTypeMapEntry("int32") },
            { "Int64", new GoTypeMapEntry("int64") },
            { "uint", new GoTypeMapEntry("uint") },
            { "UInt16", new GoTypeMapEntry("uint16") },
            { "UInt32", new GoTypeMapEntry("uint32") },
            { "UInt64", new GoTypeMapEntry("uint64") },
            { "long", new GoTypeMapEntry("int64") },
            { "ulong", new GoTypeMapEntry("uint64") },
            { "float", new GoTypeMapEntry("float32") },
            { "double", new GoTypeMapEntry("float64") },
            { "decimal", new GoTypeMapEntry("float64") },
            { "Single", new GoTypeMapEntry("float32") },

            { "bool", new GoTypeMapEntry("bool") },
            { "Boolean", new GoTypeMapEntry("bool") },

            { "char", new GoTypeMapEntry("rune") },
            { "string", new GoTypeMapEntry("string") },
            { "String", new GoTypeMapEntry("string") },

            { "DateTime", new GoTypeMapEntry("time.Time", "time") },
            { "TimeSpan", new GoTypeMapEntry("time.Duration", "time") }
        };

        /// <summary>
        /// Populates the program type map with known primitive mappings.
        /// </summary>
        /// <param name="program">The conversion program to update.</param>
        public static void PopulateTypeMap(GoProgram program) {
            if (program == null) {
                throw new ArgumentNullException(nameof(program));
            }

            foreach (var pair in PrimitiveMappings) {
                if (!program.TypeMap.ContainsKey(pair.Key)) {
                    program.TypeMap.Add(pair.Key, pair.Value.GoTypeName);
                }

                if (!string.IsNullOrWhiteSpace(pair.Value.ImportPath)) {
                    program.RegisterPackageImport(pair.Value.ImportPath, pair.Value.Alias);
                    program.RegisterTypeImport(pair.Key, pair.Value.ImportPath, pair.Value.Alias);
                }
            }
        }

        /// <summary>
        /// Resolves a primitive C# type name to its Go equivalent.
        /// </summary>
        /// <param name="typeName">The C# type name or keyword.</param>
        /// <returns>The mapped Go type name, or the original name when unmapped.</returns>
        public static string GetGoTypeName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return typeName;
            }

            if (PrimitiveMappings.TryGetValue(typeName, out GoTypeMapEntry mapped)) {
                return mapped.GoTypeName;
            }

            return typeName;
        }

        /// <summary>
        /// Attempts to resolve a primitive C# type name to its Go equivalent.
        /// </summary>
        /// <param name="typeName">The C# type name or keyword.</param>
        /// <param name="mapped">The mapped Go type name.</param>
        /// <returns>True when the type was mapped.</returns>
        public static bool TryGetGoTypeName(string typeName, out string mapped) {
            if (!string.IsNullOrWhiteSpace(typeName) && PrimitiveMappings.TryGetValue(typeName, out GoTypeMapEntry entry)) {
                mapped = entry.GoTypeName;
                return true;
            }

            mapped = string.Empty;
            return false;
        }
    }
}
