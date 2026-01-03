using cs2.core;
using cs2.go.util;
using System;
using System.Collections.Generic;

namespace cs2.go {
    /// <summary>
    /// Extension methods for mapping project VariableType to Go type strings.
    /// </summary>
    public static class GoVariableType {
        /// <summary>
        /// Returns the Go type name, without generic arguments.
        /// </summary>
        /// <param name="varType">The source variable type to map.</param>
        /// <param name="program">The Go program containing type mappings.</param>
        /// <returns>The Go type name without generic arguments.</returns>
        public static string GetGoTypeName(this VariableType varType, GoProgram program) {
            string typeName = varType.TypeName;
            if (varType.GenericArgs.Count == 0) {
                if (program.TypeMap.TryGetValue(typeName, out string type)) {
                    typeName = type;
                }
            }

            int generic = typeName.IndexOf('<');
            if (generic != -1) {
                typeName = typeName.Substring(0, generic);
            }

            return typeName;
        }

        /// <summary>
        /// Returns full Go type string, handling arrays, maps, and generics.
        /// </summary>
        /// <param name="varType">The source variable type to map.</param>
        /// <param name="program">The Go program containing type mappings.</param>
        /// <param name="imports">Optional import tracker to record dependencies.</param>
        /// <returns>The Go type name, including generics and containers.</returns>
        public static string ToGoString(this VariableType varType, GoProgram program, GoImportTracker imports = null) {
            string typeName = varType.TypeName;
            if (varType.GenericArgs.Count == 0) {
                if (program.TypeMap.TryGetValue(typeName, out string type)) {
                    typeName = type;
                }
            }

            if (typeName == "Task") {
                return BuildTaskType(varType, program, imports);
            }

            if (varType.Type == VariableDataType.Array || typeName == "Array") {
                string elementType = ResolveGenericArg(varType.GenericArgs, 0, program, imports, "interface{}");
                return $"[]{elementType}";
            }

            if (varType.Type == VariableDataType.List || typeName == "List") {
                string elementType = ResolveGenericArg(varType.GenericArgs, 0, program, imports, "interface{}");
                return $"[]{elementType}";
            }

            if (varType.Type == VariableDataType.Dictionary || typeName == "Dictionary") {
                string keyType = ResolveGenericArg(varType.GenericArgs, 0, program, imports, "interface{}");
                string valueType = ResolveGenericArg(varType.GenericArgs, 1, program, imports, "interface{}");
                return $"map[{keyType}]{valueType}";
            }

            if (varType.Type == VariableDataType.Tuple) {
                return "[]interface{}";
            }

            if (typeName == "object") {
                typeName = "interface{}";
            }

            if (imports != null) {
                TrackTypeImport(program, varType.TypeName, typeName, imports);
            }

            string genArgs = BuildGenericArguments(varType.GenericArgs, program, imports);
            string resolved = typeName;

            if (!string.IsNullOrEmpty(genArgs)) {
                resolved = $"{resolved}[{genArgs}]";
            }

            if (varType.IsNullable && !resolved.StartsWith("*") && resolved != "interface{}") {
                resolved = $"*{resolved}";
            }

            return resolved;
        }

        /// <summary>
        /// Resolves generic arguments into a Go type parameter list.
        /// </summary>
        /// <param name="args">The generic argument list.</param>
        /// <param name="program">The Go program containing type mappings.</param>
        /// <param name="imports">Optional import tracker to record dependencies.</param>
        /// <returns>The Go generic argument list.</returns>
        static string BuildGenericArguments(List<VariableType> args, GoProgram program, GoImportTracker imports) {
            if (args == null || args.Count == 0) {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < args.Count; i++) {
                parts.Add(args[i].ToGoString(program, imports));
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Resolves the requested generic argument or returns a fallback.
        /// </summary>
        /// <param name="args">The generic argument list.</param>
        /// <param name="index">The index to resolve.</param>
        /// <param name="program">The Go program containing type mappings.</param>
        /// <param name="imports">Optional import tracker to record dependencies.</param>
        /// <param name="fallback">The fallback type when no argument is available.</param>
        /// <returns>The resolved Go type name.</returns>
        static string ResolveGenericArg(List<VariableType> args, int index, GoProgram program, GoImportTracker imports, string fallback) {
            if (args == null || args.Count <= index) {
                return fallback;
            }

            return args[index].ToGoString(program, imports);
        }

        /// <summary>
        /// Tracks imports based on resolved type names.
        /// </summary>
        /// <param name="program">The Go program containing import mappings.</param>
        /// <param name="dotNetName">The original .NET type name.</param>
        /// <param name="resolvedName">The resolved Go type name.</param>
        /// <param name="imports">The import tracker to update.</param>
        static void TrackTypeImport(GoProgram program, string dotNetName, string resolvedName, GoImportTracker imports) {
            if (program.TryGetTypeImport(dotNetName, out GoKnownClass mapped) && !string.IsNullOrWhiteSpace(mapped.ImportPath)) {
                imports.AddImport(mapped.ImportPath, mapped.Alias);
                return;
            }

            int dotIndex = resolvedName.IndexOf('.');
            if (dotIndex <= 0) {
                return;
            }

            string alias = resolvedName.Substring(0, dotIndex);
            if (program.TryGetPackageImport(alias, out string importPath)) {
                imports.AddImport(importPath, alias);
            }
        }

        /// <summary>
        /// Builds the Go channel representation for Task types.
        /// </summary>
        /// <param name="varType">The source variable type to map.</param>
        /// <param name="program">The Go program containing type mappings.</param>
        /// <param name="imports">Optional import tracker to record dependencies.</param>
        /// <returns>The Go channel type.</returns>
        static string BuildTaskType(VariableType varType, GoProgram program, GoImportTracker imports) {
            if (varType.GenericArgs.Count > 0) {
                string element = varType.GenericArgs[0].ToGoString(program, imports);
                return $"chan {element}";
            }

            return "chan struct{}";
        }
    }
}
