using cs2.core;
using cs2.go.util;
using System;
using System.Collections.Generic;

namespace cs2.go {
    /// <summary>
    /// Holds Go target configuration, known-class metadata and type mappings.
    /// </summary>
    public class GoProgram : ConversionProgram {
        /// <summary>
        /// Initializes a new Go program instance.
        /// </summary>
        /// <param name="rules">The conversion rules used to configure the program.</param>
        public GoProgram(ConversionRules rules)
            : base(rules) {
            Requirements = new List<GoKnownClass>();
            RequirementKeys = new HashSet<string>(StringComparer.Ordinal);
            RequirementByName = new Dictionary<string, GoKnownClass>(StringComparer.Ordinal);
            PackageImports = new Dictionary<string, string>(StringComparer.Ordinal);
            TypeImports = new Dictionary<string, GoKnownClass>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets the runtime symbol requirements gathered for the program.
        /// </summary>
        public List<GoKnownClass> Requirements { get; }

        /// <summary>
        /// Tracks requirement keys to prevent duplicates.
        /// </summary>
        HashSet<string> RequirementKeys { get; }

        /// <summary>
        /// Indexes requirements by name for quick lookups.
        /// </summary>
        Dictionary<string, GoKnownClass> RequirementByName { get; }

        /// <summary>
        /// Caches class lookups by name.
        /// </summary>
        Dictionary<string, ConversionClass> ClassByName { get; set; }

        /// <summary>
        /// Tracks the number of classes indexed.
        /// </summary>
        int ClassIndexCount { get; set; }

        /// <summary>
        /// Maps Go package aliases to their import paths.
        /// </summary>
        Dictionary<string, string> PackageImports { get; }

        /// <summary>
        /// Maps .NET type names to known Go import metadata.
        /// </summary>
        Dictionary<string, GoKnownClass> TypeImports { get; }

        /// <summary>
        /// Registers a Go package import alias.
        /// </summary>
        /// <param name="importPath">The import path.</param>
        /// <param name="alias">The optional alias.</param>
        public void RegisterPackageImport(string importPath, string alias = "") {
            if (string.IsNullOrWhiteSpace(importPath)) {
                return;
            }

            string effectiveAlias = string.IsNullOrWhiteSpace(alias) ? GetDefaultAlias(importPath) : alias;
            if (!PackageImports.ContainsKey(effectiveAlias)) {
                PackageImports.Add(effectiveAlias, importPath);
            }
        }

        /// <summary>
        /// Registers a type import mapping for a .NET type name.
        /// </summary>
        /// <param name="typeName">The .NET type name.</param>
        /// <param name="importPath">The import path that provides the type.</param>
        /// <param name="alias">Optional alias for the import.</param>
        public void RegisterTypeImport(string typeName, string importPath, string alias = "") {
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(importPath)) {
                return;
            }

            if (!TypeImports.ContainsKey(typeName)) {
                string effectiveAlias = string.IsNullOrWhiteSpace(alias) ? GetDefaultAlias(importPath) : alias;
                TypeImports.Add(typeName, new GoKnownClass(typeName, typeName, importPath, effectiveAlias));
            }
        }

        /// <summary>
        /// Attempts to resolve a package import by alias.
        /// </summary>
        /// <param name="alias">The package alias.</param>
        /// <param name="importPath">The resolved import path.</param>
        /// <returns>True when an import path was found.</returns>
        public bool TryGetPackageImport(string alias, out string importPath) {
            if (!string.IsNullOrWhiteSpace(alias) && PackageImports.TryGetValue(alias, out string path)) {
                importPath = path;
                return true;
            }

            importPath = string.Empty;
            return false;
        }

        /// <summary>
        /// Attempts to resolve a type import mapping by .NET type name.
        /// </summary>
        /// <param name="typeName">The .NET type name.</param>
        /// <param name="knownClass">The known class mapping.</param>
        /// <returns>True when a mapping was found.</returns>
        public bool TryGetTypeImport(string typeName, out GoKnownClass knownClass) {
            if (!string.IsNullOrWhiteSpace(typeName) && TypeImports.TryGetValue(typeName, out GoKnownClass mapped)) {
                knownClass = mapped;
                return true;
            }

            knownClass = null;
            return false;
        }

        /// <summary>
        /// Adds a runtime requirement if it has not already been included.
        /// </summary>
        /// <param name="requirement">The runtime class requirement to add.</param>
        internal void AddRequirement(GoKnownClass requirement) {
            if (requirement == null) {
                throw new ArgumentNullException(nameof(requirement));
            }

            string key = BuildRequirementKey(requirement);
            if (!RequirementKeys.Add(key)) {
                return;
            }

            Requirements.Add(requirement);

            if (!RequirementByName.ContainsKey(requirement.Name)) {
                RequirementByName.Add(requirement.Name, requirement);
            }
        }

        /// <summary>
        /// Attempts to resolve a known class requirement by name.
        /// </summary>
        /// <param name="name">The requirement name.</param>
        /// <param name="knownClass">The resolved known class.</param>
        /// <returns>True when a requirement was found.</returns>
        public bool TryGetRequirement(string name, out GoKnownClass knownClass) {
            if (string.IsNullOrWhiteSpace(name)) {
                knownClass = null;
                return false;
            }

            return RequirementByName.TryGetValue(name, out knownClass);
        }

        /// <summary>
        /// Registers a conversion class with indexing support.
        /// </summary>
        /// <param name="cl">The class to register.</param>
        public void RegisterClass(ConversionClass cl) {
            if (cl == null) {
                return;
            }

            Classes.Add(cl);

            if (ClassByName == null) {
                return;
            }

            if (!string.IsNullOrWhiteSpace(cl.Name) && !ClassByName.ContainsKey(cl.Name)) {
                ClassByName.Add(cl.Name, cl);
            }

            ClassIndexCount = Classes.Count;
        }

        /// <summary>
        /// Resolves a conversion class by name.
        /// </summary>
        /// <param name="name">The class name.</param>
        /// <returns>The resolved class, or null if not found.</returns>
        public ConversionClass GetClassByName(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return null;
            }

            EnsureClassIndex();

            if (ClassByName != null && ClassByName.TryGetValue(name, out ConversionClass cl)) {
                return cl;
            }

            return null;
        }

        /// <summary>
        /// Configures the Go program with standard library mappings and native remaps.
        /// </summary>
        public void AddDotNet() {
            BuildTypeMap();
            BuildNativeRemap();

            var registrar = new GoRuntimeRequirementRegistrar();
            registrar.Register(this);
        }

        /// <summary>
        /// Ensures the class index is initialized and up to date.
        /// </summary>
        void EnsureClassIndex() {
            if (ClassByName == null) {
                ClassByName = new Dictionary<string, ConversionClass>(StringComparer.Ordinal);
                ClassIndexCount = 0;
            }

            if (ClassIndexCount == Classes.Count) {
                return;
            }

            ClassByName.Clear();
            for (int i = 0; i < Classes.Count; i++) {
                ConversionClass cl = Classes[i];
                if (cl == null || string.IsNullOrWhiteSpace(cl.Name)) {
                    continue;
                }
                if (!ClassByName.ContainsKey(cl.Name)) {
                    ClassByName.Add(cl.Name, cl);
                }
            }

            ClassIndexCount = Classes.Count;
        }

        /// <summary>
        /// Builds a unique key for a requirement.
        /// </summary>
        /// <param name="requirement">The requirement to key.</param>
        /// <returns>The key string.</returns>
        static string BuildRequirementKey(GoKnownClass requirement) {
            return $"{requirement.Name}|{requirement.GoName}|{requirement.ImportPath}";
        }

        /// <summary>
        /// Builds native remaps for core runtime classes and helpers.
        /// </summary>
        void BuildNativeRemap() {
            ConversionClass clArray = MakeClass("Array");
            RegisterClass(clArray);

            ConversionClass clString = MakeClass("string");
            RegisterClass(clString);

            ConversionClass clConsole = MakeClass("Console");
            MakeGoFunction("WriteLine", "Println", clConsole, "", "fmt");
            MakeGoFunction("Write", "Print", clConsole, "", "fmt");
            RegisterClass(clConsole);

            ConversionClass clMath = MakeClass("Math");
            MakeGoFunction("Abs", "Abs", clMath, "", "math");
            MakeGoFunction("Round", "Round", clMath, "", "math");
            MakeGoFunction("Floor", "Floor", clMath, "", "math");
            MakeGoFunction("Ceiling", "Ceil", clMath, "", "math");
            MakeGoFunction("Sqrt", "Sqrt", clMath, "", "math");
            MakeGoFunction("Pow", "Pow", clMath, "", "math");
            MakeGoVariable("PI", "Pi", clMath, "float64", "math");
            RegisterClass(clMath);
        }

        /// <summary>
        /// Builds the C# to Go type name map for the runtime.
        /// </summary>
        void BuildTypeMap() {
            GoTypeMap.PopulateTypeMap(this);
        }

        /// <summary>
        /// Creates a native conversion class for a runtime type name.
        /// </summary>
        /// <param name="name">The runtime type name.</param>
        /// <returns>The created conversion class.</returns>
        ConversionClass MakeClass(string name) {
            ConversionClass cl = new ConversionClass();
            cl.Name = name;
            cl.IsNative = true;
            return cl;
        }

        /// <summary>
        /// Adds a remapped function member to a native conversion class.
        /// </summary>
        /// <param name="name">The original member name.</param>
        /// <param name="remap">The Go name override.</param>
        /// <param name="cl">The class receiving the member.</param>
        /// <param name="type">The optional return type override.</param>
        /// <param name="remapPackage">Optional Go package name for the call target.</param>
        void MakeGoFunction(string name, string remap, ConversionClass cl, string type = "", string remapPackage = "") {
            ConversionFunction fn = new ConversionFunction();
            fn.Name = name;
            fn.Remap = remap;
            fn.RemapClass = remapPackage;
            if (!string.IsNullOrEmpty(type)) {
                fn.ReturnType = VariableUtil.GetVarType(type);
            }
            cl.Functions.Add(fn);
        }

        /// <summary>
        /// Adds a remapped variable member to a native conversion class.
        /// </summary>
        /// <param name="name">The original member name.</param>
        /// <param name="remap">The Go name override.</param>
        /// <param name="cl">The class receiving the member.</param>
        /// <param name="type">The variable type name.</param>
        /// <param name="remapPackage">Optional Go package name for the access target.</param>
        void MakeGoVariable(string name, string remap, ConversionClass cl, string type, string remapPackage = "") {
            ConversionVariable var = new ConversionVariable();
            var.Name = name;
            var.Remap = remap;
            var.RemapClass = remapPackage;
            var.VarType = VariableUtil.GetVarType(type);
            cl.Variables.Add(var);
        }

        /// <summary>
        /// Derives a default alias from an import path.
        /// </summary>
        /// <param name="importPath">The import path to inspect.</param>
        /// <returns>The inferred alias.</returns>
        static string GetDefaultAlias(string importPath) {
            int lastSlash = importPath.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < importPath.Length - 1) {
                return importPath.Substring(lastSlash + 1);
            }

            return importPath;
        }
    }
}
