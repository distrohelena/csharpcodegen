using cs2.core;
using Nucleus;
using System;
using System.Reflection;
using cs2.ts.util;

namespace cs2.ts {
    /// <summary>
    /// Holds TypeScript target configuration, known-class metadata and type mappings.
    /// Responsible for wiring the TS runtime symbols and native remaps used by the converter.
    /// </summary>
    public class TypeScriptProgram : ConversionProgram {
        /// <summary>
        /// Initializes a new TypeScript program instance.
        /// </summary>
        /// <param name="rules">The conversion rules used to configure the program.</param>
        public TypeScriptProgram(ConversionRules rules)
            : base(rules) {
            Requirements = new List<TypeScriptKnownClass>();
            RequirementKeys = new HashSet<string>(StringComparer.Ordinal);
            RequirementByName = new Dictionary<string, TypeScriptKnownClass>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets the runtime symbol requirements gathered for the program.
        /// </summary>
        public List<TypeScriptKnownClass> Requirements { get; private set; }

        /// <summary>
        /// Tracks requirement keys to prevent duplicates.
        /// </summary>
        HashSet<string> RequirementKeys { get; }

        /// <summary>
        /// Indexes requirements by name for quick lookups.
        /// </summary>
        Dictionary<string, TypeScriptKnownClass> RequirementByName { get; }

        /// <summary>
        /// Caches class lookups by name.
        /// </summary>
        Dictionary<string, ConversionClass> ClassByName { get; set; }

        /// <summary>
        /// Tracks the number of classes indexed.
        /// </summary>
        int ClassIndexCount { get; set; }

        /// <summary>
        /// Adds a runtime requirement if it has not already been included.
        /// </summary>
        /// <param name="requirement">The runtime class requirement to add.</param>
        internal void AddRequirement(TypeScriptKnownClass requirement) {
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
        public bool TryGetRequirement(string name, out TypeScriptKnownClass knownClass) {
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
        static string BuildRequirementKey(TypeScriptKnownClass requirement) {
            return $"{requirement.Name}|{requirement.Path}";
        }

        /// <summary>
        /// Configures the TypeScript program with .NET-like runtime symbols and mappings for the given environment.
        /// Loads symbol JSON from .net.ts, sets up native remaps, and populates <see cref="Classes"/>.
        /// </summary>
        /// <param name="env">The target TypeScript runtime environment.</param>
        public void AddDotNet(TypeScriptEnvironment env) {
            buildTypeMap();

            buildNativeRemap();

            buildDotNetData();

            var registrar = new TypeScriptRuntimeRequirementRegistrar();
            registrar.Register(this, env);

            var nativeBuilder = new TypeScriptNativeClassBuilder();
            nativeBuilder.BuildNativeClasses(this, Requirements);
        }

        /// <summary>
        /// Creates a native conversion class for a runtime type name.
        /// </summary>
        /// <param name="name">The runtime type name.</param>
        /// <returns>The created conversion class.</returns>
        ConversionClass makeClass(string name) {
            ConversionClass cl = new ConversionClass();
            cl.Name = name;
            cl.IsNative = true;

            makeTypeScriptFunction("ToString", "toString", cl);

            return cl;
        }

        /// <summary>
        /// Adds a remapped function member to a native conversion class.
        /// </summary>
        /// <param name="name">The original member name.</param>
        /// <param name="remap">The TypeScript name override.</param>
        /// <param name="cl">The class receiving the member.</param>
        /// <param name="type">The optional return type override.</param>
        /// <param name="remapCl">Optional class name to remap the call target.</param>
        void makeTypeScriptFunction(string name, string remap, ConversionClass cl, string type = "", string remapCl = "") {
            ConversionFunction fnToString = new ConversionFunction();
            fnToString.Name = name;
            fnToString.Remap = remap;
            fnToString.RemapClass = remapCl;
            if (!string.IsNullOrEmpty(type)) {
                fnToString.ReturnType = VariableUtil.GetVarType(type);
            }
            cl.Functions.Add(fnToString);
        }

        /// <summary>
        /// Adds a remapped variable member to a native conversion class.
        /// </summary>
        /// <param name="name">The original member name.</param>
        /// <param name="remap">The TypeScript name override.</param>
        /// <param name="cl">The class receiving the member.</param>
        /// <param name="type">The variable type name.</param>
        void makeTypeScriptVariable(string name, string remap, ConversionClass cl, string type) {
            ConversionVariable fnToString = new ConversionVariable();
            fnToString.Name = name;
            fnToString.Remap = remap;
            fnToString.VarType = VariableUtil.GetVarType(type);
            cl.Variables.Add(fnToString);
        }

        /// <summary>
        /// Builds native remaps for core runtime classes and helpers.
        /// </summary>
        void buildNativeRemap() {
            ConversionClass clArray = makeClass("Array");
            RegisterClass(clArray);
            makeTypeScriptVariable("Length", "length", clArray, "int");
            makeTypeScriptFunction("Copy", "copy", clArray, "", "NativeArrayUtil");
            makeTypeScriptFunction("SequenceEqual", "sequenceEqual", clArray, "", "NativeArrayUtil");

            ConversionClass clnumber = makeClass("number");
            RegisterClass(clnumber);

            ConversionClass clNumber = makeClass("Number");
            makeTypeScriptFunction("MaxValue", "MAX_VALUE", clNumber, "int");
            RegisterClass(clNumber);

            ConversionClass clUint = makeClass("uint");
            makeTypeScriptFunction("Parse", "parse", clUint, "uint");
            RegisterClass(clUint);

            ConversionClass clString = makeClass("string");
            makeTypeScriptFunction("Length", "length", clString, "int");
            makeTypeScriptFunction("IndexOf", "indexOf", clString, "string");
            makeTypeScriptFunction("Replace", "replace", clString, "string");
            makeTypeScriptFunction("Remove", "slice", clString, "string");
            makeTypeScriptFunction("StartsWith", "startsWith", clString, "string");
            makeTypeScriptFunction("Split", "split", clString, "string");
            makeTypeScriptFunction("Substring", "substring", clString, "string");
            makeTypeScriptFunction("IsNullOrEmpty", "isNullOrEmpty", clString, "string");
            RegisterClass(clString);

            ConversionClass clStringUpper = makeClass("String");
            makeTypeScriptFunction("IsNullOrEmpty", "isNullOrEmpty", clStringUpper, "string", "NativeStringUtil");
            RegisterClass(clStringUpper);

            ConversionClass clBool = makeClass("boolean");
            RegisterClass(clBool);

            ConversionClass clMath = makeClass("Math");
            makeTypeScriptFunction("Round", "round", clMath, "int");
            RegisterClass(clMath);

            ConversionClass clUint8Array = makeClass("Uint8Array");
            makeTypeScriptVariable("Length", "length", clUint8Array, "int");
            RegisterClass(clUint8Array);
        }

        /// <summary>
        /// Ensures .net.ts dependencies are installed, then runs the symbol extractor to generate JSON
        /// that describes available TS classes/interfaces/enums. This JSON is consumed to populate Requirements.
        /// </summary>
        void buildDotNetData() {
            string startFolder = AssemblyUtil.GetStartFolder();
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string assemblyFolder = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrWhiteSpace(assemblyFolder)) {
                throw new InvalidOperationException("Unable to resolve assembly directory.");
            }
            string dotNetFolder = Path.Combine(startFolder, ".net.ts");
            if (!Directory.Exists(dotNetFolder)) {
                dotNetFolder = Path.Combine(assemblyFolder, ".net.ts");
            }

            var request = new TypeScriptRuntimeMetadataRequest {
                RuntimeDirectory = dotNetFolder,
                EnsureDependencies = true,
                ThrowOnError = true,
                ForwardOutput = true,
                InstallTimeoutMinutes = 3,
                Logger = Console.WriteLine
            };

            TypeScriptRuntimeMetadata.EnsureRuntimeMetadata(request);
        }
        /// <summary>
        /// Builds the C# to TypeScript type name map for the runtime.
        /// </summary>
        void buildTypeMap() {
            TypeScriptTypeMap.PopulateTypeMap(this);
        }
    }
}



