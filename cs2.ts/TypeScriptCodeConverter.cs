using cs2.core;
using cs2.core.Pipeline;
using cs2.ts.util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Nucleus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace cs2.ts {
    /// <summary>
    /// Converts C# code to TypeScript using Roslyn and TypeScriptProgram metadata.
    /// Writes TS output and copies required runtime helpers from .net.ts.
    /// </summary>
    public class TypeScriptCodeConverter : CodeConverter {
        /// <summary>
        /// Stores the resolved assembly name for placeholder replacement.
        /// </summary>
        string assemblyName;
        /// <summary>
        /// Stores the resolved assembly version for placeholder replacement.
        /// </summary>
        string version;
        /// <summary>
        /// Stores the resolved target framework for placeholder replacement.
        /// </summary>
        string targetFramework;

        /// <summary>
        /// Handles TypeScript-specific conversion of syntax nodes.
        /// </summary>
        TypeScriptConversiorProcessor conversion;
        /// <summary>
        /// Holds the TypeScript conversion program metadata.
        /// </summary>
        TypeScriptProgram tsProgram;
        /// <summary>
        /// Stores the resolved conversion options used during conversion.
        /// </summary>
        readonly TypeScriptConversionOptions conversionOptions;
        /// <summary>
        /// Stores the preprocessor symbols used for conversion.
        /// </summary>
        readonly string[] preprocessorSymbols;
        /// <summary>
        /// Indicates whether project-defined preprocessor symbols are retained.
        /// </summary>
        readonly bool includeProjectPreprocessorSymbols;
        /// <summary>
        /// Tracks reflection runtime import requirements.
        /// </summary>
        TypeScriptReflectionImportTracker ReflectionImportTracker;
        /// <summary>
        /// Emits TypeScript classes, interfaces, and enums.
        /// </summary>
        TypeScriptClassEmitter ClassEmitter;
        /// <summary>
        /// Type names that require the reflection runtime.
        /// </summary>
        static readonly HashSet<string> ReflectionTypeNames = new HashSet<string>(StringComparer.Ordinal) {
            "Type",
            "BindingFlags",
            "PropertyInfo",
            "MethodInfo",
            "FieldInfo",
            "ConstructorInfo",
            "MemberInfo",
            "ParameterInfo",
            "Assembly",
            "AssemblyName",
            "TypeInfo"
        };
        /// <summary>
        /// Separators used to split composite type names when scanning for reflection usage.
        /// </summary>
        static readonly char[] ReflectionTypeTokenSeparators = new[] {
            '<', '>', ',', '[', ']', '(', ')', '?', '&', '*', ' ', '\t', '\r', '\n'
        };

        /// <summary>
        /// Creates a new converter for the given environment and options.
        /// </summary>
        /// <param name="rules">The conversion rules shared across backends.</param>
        /// <param name="env">The TypeScript runtime environment.</param>
        /// <param name="options">Optional conversion options.</param>
        public TypeScriptCodeConverter(ConversionRules rules, TypeScriptEnvironment env, TypeScriptConversionOptions options = null)
            : base(rules) {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string baseDirectory = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrWhiteSpace(baseDirectory)) {
                throw new InvalidOperationException("Unable to resolve converter base directory.");
            }

            Directory.SetCurrentDirectory(baseDirectory);
            this.rules = rules;

            TypeScriptConversionOptions resolvedOptions;
            if (options == null) {
                resolvedOptions = TypeScriptConversionOptions.Default.Clone();
            } else {
                resolvedOptions = options.Clone();
            }
            conversionOptions = resolvedOptions;
            TypeScriptReflectionEmitter.GlobalOptions = resolvedOptions.Reflection.Clone();

            preprocessorSymbols = BuildPreprocessorSymbols(resolvedOptions);
            includeProjectPreprocessorSymbols = resolvedOptions.IncludeProjectDefinedPreprocessorSymbols;

            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

            EnsureRuntimeMetadata();

            workspace = MSBuildWorkspace.Create();

            tsProgram = new TypeScriptProgram(rules);
            program = tsProgram;

            tsProgram.AddDotNet(env);
            context = new ConversionContext(program);

            conversion = new TypeScriptConversiorProcessor();
            ReflectionImportTracker = new TypeScriptReflectionImportTracker();
            ClassEmitter = new TypeScriptClassEmitter(conversion, program, tsProgram, conversionOptions, ReflectionImportTracker, SortVariables);

            assemblyName = "";
            version = "";
            targetFramework = "";
        }

        /// <summary>
        /// Gets the preprocessor symbols applied during conversion.
        /// </summary>
        protected override string[] PreProcessorSymbols => preprocessorSymbols;
        /// <summary>
        /// Gets whether project-defined preprocessor symbols are preserved.
        /// </summary>
        internal bool IncludeProjectPreprocessorSymbols => includeProjectPreprocessorSymbols;
        /// <summary>
        /// Gets the internal preprocessor symbol list for pipeline stages.
        /// </summary>
        internal string[] PreprocessorSymbols => preprocessorSymbols;

        /// <summary>
        /// Builds the full set of preprocessor symbols used during conversion.
        /// </summary>
        /// <param name="options">The conversion options that may add symbols.</param>
        /// <returns>The resolved preprocessor symbol list.</returns>
        static string[] BuildPreprocessorSymbols(TypeScriptConversionOptions options) {
            HashSet<string> symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TYPESCRIPT", "CSHARP" };

            if (options.AdditionalPreprocessorSymbols != null) {
                foreach (string symbol in options.AdditionalPreprocessorSymbols) {
                    if (string.IsNullOrWhiteSpace(symbol)) {
                        continue;
                    }

                    symbols.Add(symbol.Trim());
                }
            }

            return symbols.ToArray();
        }

        /// <summary>
        /// Configures the conversion pipeline with TypeScript-specific stages.
        /// </summary>
        /// <param name="builder">The pipeline builder used to register stages.</param>
        protected override void ConfigurePipeline(ConversionPipelineBuilder builder) {
            base.ConfigurePipeline(builder);

            var preprocessorFilter = new TypeScriptPreprocessorFilterStage(this);
            var metadataStage = new TypeScriptAssemblyMetadataStage(this);

            int applyIndex = -1;
            for (int i = 0; i < builder.Stages.Count; i++) {
                if (builder.Stages[i] is ApplyPreprocessorSymbolsStage) {
                    applyIndex = i;
                    break;
                }
            }

            if (applyIndex >= 0) {
                builder.Insert(applyIndex + 1, preprocessorFilter);
                builder.Insert(applyIndex + 2, metadataStage);
            } else {
                builder.AddStage(preprocessorFilter);
                builder.AddStage(metadataStage);
            }
        }

        /// <summary>
        /// Writes the generated TypeScript file and copies runtime helpers into <paramref name="outputFolder"/>.
        /// </summary>
        /// <param name="outputFolder">The folder that receives generated output.</param>
        /// <param name="fileName">The TypeScript file name to write.</param>
        public void WriteFile(string outputFolder, string fileName) {
            var replacements = new Dictionary<string, string>() {
                { "ASSEMBLY_NAME", assemblyName },
                { "ASSEMBLY_VERSION", version },
                { "ASSEMBLY_DESCRIPTION", targetFramework }
            };

            Assembly entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly == null) {
                throw new InvalidOperationException("Unable to resolve entry assembly for runtime metadata.");
            }

            string entryDirectory = Path.GetDirectoryName(entryAssembly.Location);
            if (string.IsNullOrWhiteSpace(entryDirectory)) {
                throw new InvalidOperationException("Unable to resolve entry assembly directory.");
            }

            string rootPath = Path.Combine(entryDirectory, ".net.ts");
            DirectoryUtil.RecursiveCopy(new DirectoryInfo(rootPath), new DirectoryInfo(outputFolder),
                (file, reader, writer) => {
                    string source = reader.ReadToEnd();
                    source = Utils.ReplacePlaceholders(source, replacements);

                    writer.Write(source);
            },
                (file) => {
                    return !file.FullName.EndsWith(".json") && !file.FullName.EndsWith("extractor.js");
            });

            string outputFile = Path.Combine(outputFolder, fileName);
            string outputDir = Path.GetDirectoryName(outputFile);
            if (string.IsNullOrWhiteSpace(outputDir)) {
                throw new InvalidOperationException("Unable to resolve output directory.");
            }
            Directory.CreateDirectory(outputDir);

            if (File.Exists(outputFile)) {
                File.Delete(outputFile);
            }

            using StringWriter stringWriter = new StringWriter();
            TypeScriptOutputWriter output = new TypeScriptOutputWriter(stringWriter);

            ValidateReflectionUsage();
            ComputeReflectionImports();
            foreach (var pair in tsProgram.Requirements) {
                if (pair is TypeScriptGenericKnownClass gen) {
                    string imports = "";

                    for (int i = gen.Start; i < gen.TotalImports; i++) {
                        if (i == gen.TotalImports - 1) {
                            imports += gen.Name + i;
                        } else if (i == gen.Start) {
                            imports += gen.Name + ", ";
                        } else {
                            imports += gen.Name + i + ", ";
                        }
                    }

                    output.WriteLine($"import type {{ {imports} }} from \"{pair.Path}\";");
                } else {
                    if (string.IsNullOrEmpty(pair.Replacement)) {
                        output.WriteLine($"import {{ {pair.Name} }} from \"{pair.Path}\";");
                    } else {
                        output.WriteLine($"import {{ {pair.Name} as {pair.Replacement} }} from \"{pair.Path}\";");
                    }
                }
            }

            TypeScriptReflectionEmitter.EmitRuntimeImport(output.Writer,
                ReflectionImportTracker.NeedsTypeImport,
                ReflectionImportTracker.NeedsEnumImport,
                ReflectionImportTracker.NeedsMetadataImport,
                conversionOptions.Reflection);
            output.WriteLine();

            writeOutput(output);

            output.WriteLine();

            string formatted = TypeScriptCodeFormatter.Format(stringWriter.ToString(), rootPath, Console.WriteLine);
            File.WriteAllText(outputFile, formatted);

            WriteStrictTsConfig(outputFolder);
        }

        /// <summary>
        /// Stores assembly metadata for placeholder replacement in emitted assets.
        /// </summary>
        /// <param name="assembly">The resolved assembly name.</param>
        /// <param name="resolvedVersion">The resolved assembly version.</param>
        /// <param name="framework">The resolved target framework.</param>
        internal void SetAssemblyMetadata(string assembly, string resolvedVersion, string framework) {
            if (assembly == null) {
                assemblyName = string.Empty;
            } else {
                assemblyName = assembly;
            }
            if (resolvedVersion == null) {
                version = string.Empty;
            } else {
                version = resolvedVersion;
            }
            if (framework == null) {
                targetFramework = string.Empty;
            } else {
                targetFramework = framework;
            }
        }

        /// <summary>
        /// Preprocesses class members to apply name remaps and line caching.
        /// </summary>
        /// <param name="cl">The class to preprocess.</param>
        void preprocessClass(ConversionClass cl) {
            if (cl.IsNative) {
                return;
            }

            var functions = cl.Functions.Where(c => !c.IsConstructor).ToList();
            for (int j = 0; j < functions.Count; j++) {
                ConversionFunction fn = functions[j];

                if (fn.Name == "Dispose") {
                    fn.Remap = "dispose";
                }

                fn.WriteLines(conversion, program, cl);
            }
        }

        /// <summary>
        /// Computes which reflection runtime imports are required for the output file.
        /// </summary>
        void ComputeReflectionImports() {
            ReflectionImportTracker.Reset();

            if (!conversionOptions.Reflection.EnableReflection) {
                return;
            }

            foreach (var cl in program.Classes) {
                if (cl.IsNative) {
                    continue;
                }

                if (cl.TypeSymbol is not INamedTypeSymbol) {
                    continue;
                }

                switch (cl.DeclarationType) {
                    case MemberDeclarationType.Enum:
                        ReflectionImportTracker.NeedsEnumImport = true;
                        break;
                    case MemberDeclarationType.Interface:
                        ReflectionImportTracker.NeedsMetadataImport = true;
                        break;
                    case MemberDeclarationType.Delegate:
                        ReflectionImportTracker.NeedsMetadataImport = true;
                        break;
                    default:
                        ReflectionImportTracker.NeedsTypeImport = true;
                        break;
                }

                if (ReflectionImportTracker.NeedsTypeImport &&
                    ReflectionImportTracker.NeedsEnumImport &&
                    ReflectionImportTracker.NeedsMetadataImport) {
                    break;
                }
            }
        }
        /// <summary>
        /// Ensures reflection types are not referenced when reflection output is disabled.
        /// </summary>
        void ValidateReflectionUsage() {
            if (conversionOptions.Reflection.EnableReflection) {
                return;
            }

            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);

            if (program?.Classes == null) {
                return;
            }

            foreach (var cl in program.Classes) {
                if (cl == null || cl.IsNative) {
                    continue;
                }

                ScanForReflectionTypes(cl, found);
            }

            if (found.Count > 0) {
                throw new InvalidOperationException($"Reflection is disabled, but reflection types were referenced: {string.Join(", ", found)}");
            }
        }

        void ScanForReflectionTypes(ConversionClass cl, HashSet<string> found) {
            if (cl == null) {
                return;
            }

            if (cl.Variables != null) {
                foreach (var variable in cl.Variables) {
                    ScanForReflectionTypes(variable?.VarType, found);
                }
            }

            if (cl.Functions != null) {
                foreach (var function in cl.Functions) {
                    if (function == null) {
                        continue;
                    }

                    if (function.InParameters != null) {
                        foreach (var parameter in function.InParameters) {
                            ScanForReflectionTypes(parameter?.VarType, found);
                        }
                    }

                    if (function.ReturnType != null) {
                        ScanForReflectionTypes(function.ReturnType, found);
                    }
                }
            }

            if (cl.ReferencedClasses != null) {
                foreach (var reference in cl.ReferencedClasses) {
                    AddReflectionTypeHit(reference, found);
                }
            }
        }

        void ScanForReflectionTypes(VariableType type, HashSet<string> found) {
            if (type == null) {
                return;
            }

            AddReflectionTypeHit(type.TypeName, found);

            if (type.Args != null) {
                foreach (var arg in type.Args) {
                    ScanForReflectionTypes(arg, found);
                }
            }

            if (type.GenericArgs != null) {
                foreach (var arg in type.GenericArgs) {
                    ScanForReflectionTypes(arg, found);
                }
            }
        }

        static void AddReflectionTypeHit(string typeName, HashSet<string> found) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return;
            }

            string reflectionName = FindReflectionTypeName(typeName);
            if (!string.IsNullOrWhiteSpace(reflectionName)) {
                found.Add(reflectionName);
            }
        }

        static string FindReflectionTypeName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return null;
            }

            string[] tokens = typeName.Split(ReflectionTypeTokenSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++) {
                string token = tokens[i];
                if (ReflectionTypeNames.Contains(token)) {
                    return token;
                }
            }

            foreach (string name in ReflectionTypeNames) {
                for (int i = 0; i < tokens.Length; i++) {
                    if (tokens[i].EndsWith("." + name, StringComparison.Ordinal)) {
                        return name;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Propagates async flags across related and derived class methods.
        /// </summary>
        void PropagateAsyncOverrides() {
            if (program.Classes == null || program.Classes.Count == 0) {
                return;
            }

            Dictionary<string, List<ConversionClass>> classIndex = BuildClassNameIndex(program.Classes);
            Dictionary<string, List<ConversionClass>> derivedMap = BuildDerivedClassMap(program.Classes);
            Dictionary<string, List<ConversionClass>> relatedMap = BuildRelatedClassMap(program.Classes, classIndex, derivedMap);

            PropagateAsyncInRelatedClasses(program.Classes, relatedMap);
            PropagateAsyncInDerivedClasses(program.Classes, derivedMap);
        }

        /// <summary>
        /// Builds a lookup of class names to conversion classes that share that name.
        /// </summary>
        /// <param name="classes">The classes to index by name.</param>
        /// <returns>The lookup mapping names to class lists.</returns>
        Dictionary<string, List<ConversionClass>> BuildClassNameIndex(IReadOnlyList<ConversionClass> classes) {
            Dictionary<string, List<ConversionClass>> index = new Dictionary<string, List<ConversionClass>>(StringComparer.Ordinal);
            if (classes == null) {
                return index;
            }

            for (int i = 0; i < classes.Count; i++) {
                ConversionClass cl = classes[i];
                if (cl == null || string.IsNullOrWhiteSpace(cl.Name)) {
                    continue;
                }

                AddClassToMap(index, cl.Name, cl);
            }

            return index;
        }

        /// <summary>
        /// Builds a lookup of base class names to direct derived classes.
        /// </summary>
        /// <param name="classes">The conversion classes to analyze.</param>
        /// <returns>The lookup mapping base names to derived class lists.</returns>
        Dictionary<string, List<ConversionClass>> BuildDerivedClassMap(IReadOnlyList<ConversionClass> classes) {
            Dictionary<string, List<ConversionClass>> derivedMap = new Dictionary<string, List<ConversionClass>>(StringComparer.Ordinal);
            if (classes == null) {
                return derivedMap;
            }

            for (int i = 0; i < classes.Count; i++) {
                ConversionClass cl = classes[i];
                if (cl == null || cl.Extensions == null) {
                    continue;
                }

                for (int j = 0; j < cl.Extensions.Count; j++) {
                    string extension = cl.Extensions[j];
                    if (string.IsNullOrWhiteSpace(extension)) {
                        continue;
                    }

                    AddClassToMap(derivedMap, extension, cl);
                }
            }

            return derivedMap;
        }

        /// <summary>
        /// Builds a lookup of class names to directly related classes (base and derived).
        /// </summary>
        /// <param name="classes">The conversion classes to analyze.</param>
        /// <param name="classIndex">Lookup of class names to class lists.</param>
        /// <param name="derivedMap">Lookup of base names to derived classes.</param>
        /// <returns>The lookup mapping class names to related class lists.</returns>
        Dictionary<string, List<ConversionClass>> BuildRelatedClassMap(
            IReadOnlyList<ConversionClass> classes,
            Dictionary<string, List<ConversionClass>> classIndex,
            Dictionary<string, List<ConversionClass>> derivedMap) {
            Dictionary<string, List<ConversionClass>> relatedMap = new Dictionary<string, List<ConversionClass>>(StringComparer.Ordinal);
            if (classes == null) {
                return relatedMap;
            }

            for (int i = 0; i < classes.Count; i++) {
                ConversionClass cl = classes[i];
                if (cl == null || string.IsNullOrWhiteSpace(cl.Name)) {
                    continue;
                }

                if (derivedMap.TryGetValue(cl.Name, out List<ConversionClass> derivedClasses)) {
                    for (int j = 0; j < derivedClasses.Count; j++) {
                        AddClassToMap(relatedMap, cl.Name, derivedClasses[j]);
                    }
                }

                if (cl.Extensions == null) {
                    continue;
                }

                for (int j = 0; j < cl.Extensions.Count; j++) {
                    string extension = cl.Extensions[j];
                    if (string.IsNullOrWhiteSpace(extension)) {
                        continue;
                    }

                    if (!classIndex.TryGetValue(extension, out List<ConversionClass> baseClasses)) {
                        continue;
                    }

                    for (int k = 0; k < baseClasses.Count; k++) {
                        AddClassToMap(relatedMap, cl.Name, baseClasses[k]);
                    }
                }
            }

            return relatedMap;
        }

        /// <summary>
        /// Propagates async flags to matching methods in related classes.
        /// </summary>
        /// <param name="classes">The classes to scan for async methods.</param>
        /// <param name="relatedMap">Lookup of class names to related classes.</param>
        void PropagateAsyncInRelatedClasses(IReadOnlyList<ConversionClass> classes, Dictionary<string, List<ConversionClass>> relatedMap) {
            if (classes == null || relatedMap == null) {
                return;
            }

            for (int j = 0; j < classes.Count; j++) {
                ConversionClass cl = classes[j];
                if (cl == null) {
                    continue;
                }

                for (int i = 0; i < cl.Functions.Count; i++) {
                    ConversionFunction fn = cl.Functions[i];
                    if (!fn.IsAsync) {
                        continue;
                    }

                    if (!relatedMap.TryGetValue(cl.Name, out List<ConversionClass> relatedClasses)) {
                        continue;
                    }

                    string functionName = fn.Name;
                    for (int k = 0; k < relatedClasses.Count; k++) {
                        ConversionClass relatedClass = relatedClasses[k];
                        if (relatedClass == null || relatedClass == cl) {
                            continue;
                        }

                        ConversionFunction relatedFn = relatedClass.Functions.FirstOrDefault(f => f.Name == functionName);
                        if (relatedFn != null) {
                            relatedFn.IsAsync = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Propagates async flags to matching methods in derived classes.
        /// </summary>
        /// <param name="classes">The classes to scan for async methods.</param>
        /// <param name="derivedMap">Lookup of base names to derived classes.</param>
        void PropagateAsyncInDerivedClasses(IReadOnlyList<ConversionClass> classes, Dictionary<string, List<ConversionClass>> derivedMap) {
            if (classes == null || derivedMap == null) {
                return;
            }

            for (int j = 0; j < classes.Count; j++) {
                ConversionClass cl = classes[j];
                if (cl == null) {
                    continue;
                }

                for (int i = 0; i < cl.Functions.Count; i++) {
                    ConversionFunction fn = cl.Functions[i];
                    if (!fn.IsAsync) {
                        continue;
                    }

                    if (!derivedMap.TryGetValue(cl.Name, out List<ConversionClass> derivedClasses)) {
                        continue;
                    }

                    string functionName = fn.Name;
                    for (int k = 0; k < derivedClasses.Count; k++) {
                        ConversionClass derivedClass = derivedClasses[k];
                        if (derivedClass == null || derivedClass == cl) {
                            continue;
                        }

                        ConversionFunction derivedFn = derivedClass.Functions.FirstOrDefault(f => f.Name == functionName);
                        if (derivedFn != null) {
                            derivedFn.IsAsync = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds a class to a map keyed by class name, avoiding duplicates.
        /// </summary>
        /// <param name="map">The map to update.</param>
        /// <param name="key">The class name key.</param>
        /// <param name="value">The class to add.</param>
        static void AddClassToMap(Dictionary<string, List<ConversionClass>> map, string key, ConversionClass value) {
            if (map == null || string.IsNullOrWhiteSpace(key) || value == null) {
                return;
            }

            if (!map.TryGetValue(key, out List<ConversionClass> list)) {
                list = new List<ConversionClass>();
                map.Add(key, list);
            }

            if (!list.Contains(value)) {
                list.Add(value);
            }
        }

        /// <summary>
        /// Writes the full TypeScript output for the current program.
        /// </summary>
        /// <param name="writer">The writer receiving the output.</param>
        void writeOutput(TypeScriptOutputWriter writer) {
            SortProgram();

            // pre-process
            int steps = 3;
            for (int i = 0; i < steps; i++) {
                for (int j = 0; j < program.Classes.Count; j++) {
                    preprocessClass(program.Classes[j]);
                }
            }

            PropagateAsyncOverrides();

            for (int i = 0; i < program.Classes.Count; i++) {
                ClassEmitter.EmitClass(program.Classes[i], writer);
            }
        }

        /// <summary>
        /// Ensures runtime metadata JSON files exist for the bundled TypeScript runtime.
        /// </summary>
        static void EnsureRuntimeMetadata() {
            try {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string baseDir = Path.GetDirectoryName(assemblyLocation);
                if (string.IsNullOrWhiteSpace(baseDir)) {
                    Console.WriteLine("Warning: unable to resolve runtime metadata directory.");
                    return;
                }

                string runtimeDir = Path.Combine(baseDir, ".net.ts");
                if (!Directory.Exists(runtimeDir)) {
                    return;
                }

                var tsFiles = Directory.EnumerateFiles(runtimeDir, "*.ts", SearchOption.AllDirectories)
                    .Where(f => f.IndexOf("node_modules", StringComparison.OrdinalIgnoreCase) == -1)
                    .Where(f => f.IndexOf(string.Concat(Path.DirectorySeparatorChar, "src", Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == -1);

                bool missingMetadata = tsFiles.Any(f => !File.Exists(Path.ChangeExtension(f, ".json")));
                if (!missingMetadata) {
                    return;
                }

                var request = new TypeScriptRuntimeMetadataRequest {
                    RuntimeDirectory = runtimeDir,
                    EnsureDependencies = true,
                    ThrowOnError = false,
                    ForwardOutput = true,
                    InstallTimeoutMinutes = 3,
                    Logger = Console.WriteLine
                };

                TypeScriptRuntimeMetadata.EnsureRuntimeMetadata(request);
            } catch (Exception ex) {
                Console.WriteLine($"Warning: unable to regenerate runtime metadata: {ex.Message}");
            }
        }
        /// <summary>
        /// Preprocesses expressions before conversion to cache analysis results.
        /// </summary>
        /// <param name="model">The semantic model for the document.</param>
        /// <param name="member">The member being processed.</param>
        /// <param name="context">The active conversion context.</param>
        protected override void PreProcessExpression(SemanticModel model, MemberDeclarationSyntax member, ConversionContext context) {
            ConversionPreProcessor.PreProcessExpression(model, context, member);
        }

        /// <summary>
        /// Applies TypeScript-specific class processing after base conversion.
        /// </summary>
        /// <param name="cl">The class being processed.</param>
        /// <param name="program">The conversion program containing the class.</param>
        protected override void ProcessClass(ConversionClass cl, ConversionProgram program) {
            conversion.ProcessClass(cl, program);
        }

        /// <summary>
        /// Writes a strict tsconfig configuration alongside generated output.
        /// </summary>
        /// <param name="outputFolder">The output folder that receives the config.</param>
        void WriteStrictTsConfig(string outputFolder) {
            string configPath = Path.Combine(outputFolder, "tsconfig.strict.json");
            string configContent = @"{
    ""compilerOptions"": {
        ""target"": ""ES2020"",
        ""module"": ""es2020"",
        ""moduleResolution"": ""node"",
        ""strict"": true,
        ""noImplicitAny"": true,
        ""esModuleInterop"": true,
        ""skipLibCheck"": true,
        ""forceConsistentCasingInFileNames"": true,
        ""resolveJsonModule"": true,
        ""lib"": [
            ""ES2020"",
            ""DOM"",
            ""DOM.Iterable""
        ],
        ""noEmit"": true
    },
    ""include"": [
        ""./**/*.ts"",
        ""./**/*.d.ts""
    ],
    ""exclude"": [
        ""node_modules""
    ]
}";

            Directory.CreateDirectory(outputFolder);
            File.WriteAllText(configPath, configContent);
        }

    }
}
