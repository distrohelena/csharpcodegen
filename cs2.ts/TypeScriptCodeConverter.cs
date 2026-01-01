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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

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
        /// Tracks whether a type registration import is required.
        /// </summary>
        bool needsReflectionTypeImport;
        /// <summary>
        /// Tracks whether an enum registration import is required.
        /// </summary>
        bool needsReflectionEnumImport;
        /// <summary>
        /// Tracks whether a metadata registration import is required.
        /// </summary>
        bool needsReflectionMetadataImport;

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

            Stream stream = File.OpenWrite(outputFile);
            StreamWriter writer = new StreamWriter(stream);

            ComputeReflectionImports();
            writer.WriteLine("// @ts-nocheck");
            writer.WriteLine();

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

                    writer.WriteLine($"import type {{ {imports} }} from \"{pair.Path}\";");
                } else {
                    if (string.IsNullOrEmpty(pair.Replacement)) {
                        writer.WriteLine($"import {{ {pair.Name} }} from \"{pair.Path}\";");
                    } else {
                        writer.WriteLine($"import {{ {pair.Name} as {pair.Replacement} }} from \"{pair.Path}\";");
                    }
                }
            }

            TypeScriptReflectionEmitter.EmitRuntimeImport(writer, needsReflectionTypeImport, needsReflectionEnumImport, needsReflectionMetadataImport, conversionOptions.Reflection);
            writer.WriteLine();

            writeOutput(writer);

            writer.WriteLine();

            writer.Flush();
            stream.Dispose();

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
        /// Emits static initializer block for classes with static constructors.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The writer receiving the output.</param>
        void writeStaticConstructor(ConversionClass cl, StreamWriter writer) {
            var constructors = cl.Functions.Where(c => c.IsConstructor && c.IsStatic).ToList();
            if (constructors.Count == 0) {
                return;
            }

            ConversionFunction fn = constructors[0];
            writer.WriteLine("    static {");

            fn.WriteLines(conversion, program, cl, writer);

            writer.WriteLine("    }");
            writer.WriteLine($"");
        }

        /// <summary>
        /// Emits constructor overloads and factory methods for TypeScript.
        /// </summary>
        /// <param name="cl">The class being emitted.</param>
        /// <param name="writer">The writer receiving the output.</param>
        void writeConstructors(ConversionClass cl, StreamWriter writer) {
            var constructors = cl.Functions.Where(c => c.IsConstructor && !c.IsStatic).ToList();
            var classOverrides = cl.Extensions.Count(over => {
                var extCl = program.Classes.FirstOrDefault(c => c.Name == over);
                if (extCl != null) {
                    return extCl.DeclarationType != MemberDeclarationType.Interface;
                }

                return false;
            });

            if (constructors.Count == 1) {
                ConversionFunction fn = constructors[0];
                writer.Write($"    constructor(");

                for (int k = 0; k < fn.InParameters.Count; k++) {
                    var param = fn.InParameters[k];
                    string type = param.VarType.ToTypeScriptString(tsProgram);
                    string def = param.DefaultValue;
                    if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                        def = "";
                    }

                    writer.Write($"{param.Name}: {type}{def}");

                    if (k != fn.InParameters.Count - 1) {
                        writer.Write($", ");
                    }
                }

                writer.WriteLine($") {{");

                if (classOverrides > 0) {
                    writer.WriteLine($"        super();");
                }

                fn.WriteLines(conversion, program, cl, writer);

                writer.WriteLine($"    }}");
                writer.WriteLine($"");
            } else {
                string generic = cl.GetGenericArguments();

                for (int i = 0; i < constructors.Count; i++) {
                    ConversionFunction fn = constructors[i];
                    writer.Write($"    static {fn.Name}{generic}(");

                    for (int k = 0; k < fn.InParameters.Count; k++) {
                        var param = fn.InParameters[k];
                        string type = param.VarType.ToTypeScriptString(tsProgram);
                        string def = param.DefaultValue;
                        if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                            def = "";
                        }

                        writer.Write($"{param.Name}: {type}{def}");

                        if (k != fn.InParameters.Count - 1) {
                            writer.Write($", ");
                        }
                    }

                    if (cl.Name == "ClientSettings" && fn.Name == "New2") {
                        //Debugger.Break();
                    }

                    writer.WriteLine($"): {cl.Name}{generic} {{");

                    writer.WriteLine($"const __obj = new {cl.Name}();");

                    List<string> lines = new List<string>();
                    LayerContext context = new TypeScriptLayerContext((TypeScriptProgram)program);

                    int start = context.DepthClass;
                    int startFn = context.DepthFunction;

                    context.AddClass(cl);
                    context.AddFunction(new FunctionStack(fn));

                    if (fn.ArrowExpression != null) {
                        conversion.ProcessArrowExpressionClause(cl.Semantic, context, fn.ArrowExpression, lines);
                    } else if (fn.RawBlock != null) {
                        conversion.ProcessBlock(cl.Semantic, context, fn.RawBlock, lines);
                    }

                    context.PopClass(start);
                    context.PopFunction(startFn);


                    writer.Write("        ");
                    for (int k = 0; k < lines.Count; k++) {
                        string str = lines[k];
                        str = str.Replace("this.", "__obj.");

                        writer.Write(str);
                        if (str.IndexOf("\n") != -1 && k != lines.Count - 1) {
                            writer.Write("        ");
                        }
                    }

                    writer.WriteLine($"return __obj;");
                    writer.WriteLine($"    }}");
                    writer.WriteLine($"");
                }
            }
        }

        /// <summary>
        /// Writes a TypeScript field or property for the given conversion variable.
        /// </summary>
        /// <param name="cl">The owning class.</param>
        /// <param name="var">The variable to emit.</param>
        /// <param name="writer">The writer receiving the output.</param>
        /// <returns>True when output was emitted for the variable.</returns>
        bool writeVariable(ConversionClass cl, ConversionVariable var, StreamWriter writer) {
            string access = var.AccessType.ToString().ToLowerInvariant();
            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                access = "";
            }

            string type = var.VarType.ToTypeScriptString(tsProgram);

            string accessType = "";
            // TODO: implement none
            if (var.DeclarationType != MemberDeclarationType.Class) {
                accessType += " ";
                accessType += var.DeclarationType.ToString().ToLowerInvariant();
            }

            string isStatic = "";
            if (var.IsStatic) {
                isStatic = " static";
            }

            string assignment = "";
            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                if (var.IsGet && var.IsSet) {
                    writer.WriteLine($"    {access} get {var.Name}(): {type};");
                    writer.WriteLine($"    {access} set {var.Name}(val: {type});");
                } else {
                    if (var.IsGet) {
                        writer.WriteLine($"    {access}{accessType} get {var.Name}(): {type};");
                    }
                    if (var.IsSet) {
                        writer.WriteLine($"    {access}{accessType} set {var.Name}(value: {type});");
                    }
                    if (!var.IsGet && !var.IsSet) {
                        writer.WriteLine($"    {access} {var.Name}: {type};");
                    }
                }
            } else {
                if (!string.IsNullOrEmpty(var.Assignment)) {
                    string ass = var.Assignment;
                    ass = ass.Replace("=>", "").Trim();
                    assignment = $" = {ass}";
                }

                string definiteAssignment = string.IsNullOrEmpty(assignment) ? "!" : string.Empty;

                // check for property
                if (var.DeclarationType == MemberDeclarationType.Abstract) {
                    if (var.IsGet && var.IsSet) {
                        writer.WriteLine($"    {access}{isStatic} abstract get {var.Name}(): {type};");
                        writer.WriteLine($"    {access}{isStatic} abstract set {var.Name}(value: {type});");
                        return true;
                    } else if (var.IsGet) {
                        writer.WriteLine($"    {access}{isStatic} abstract get {var.Name}(): {type};");
                        return true;
                    }
                }
                if (var.IsGet && var.IsSet) {
                    writer.WriteLine($"    private{isStatic} _{var.Name}{definiteAssignment}: {type}{assignment};");
                    writer.WriteLine($"    {access}{isStatic} get {var.Name}(): {type} {{");
                    writer.WriteLine($"        return this._{var.Name};");
                    writer.WriteLine($"    }}");
                    writer.WriteLine($"    {access}{isStatic} set {var.Name}(value: {type}) {{");
                    writer.WriteLine($"        this._{var.Name} = value;");
                    writer.WriteLine($"    }}");
                    return true;
                } else if (var.IsGet) {
                    writer.WriteLine($"    private _{var.Name}{definiteAssignment}: {type}{assignment};");
                    writer.WriteLine($"    {access}{isStatic} get {var.Name}(): {type} {{");
                    writer.WriteLine($"        return this._{var.Name};");
                    writer.WriteLine($"    }}");
                    writer.WriteLine($"    private{isStatic} set {var.Name}(value: {type}) {{");
                    writer.WriteLine($"        this._{var.Name} = value;");
                    writer.WriteLine($"    }}");
                    return true;
                } else if (var.ArrowExpression != null) {
                    writer.WriteLine($"    {access}{isStatic} get {var.Name}(): {type} {{");

                    writer.Write("    ");
                    writer.Write("    ");
                    writer.Write("return ");

                    List<string> lines = new List<string>();
                    TypeScriptLayerContext context = new TypeScriptLayerContext(tsProgram);

                    int start = context.DepthClass;
                    context.AddClass(cl);
                    conversion.ProcessExpression(cl.Semantic, context, var.ArrowExpression, lines);
                    context.PopClass(start);

                    for (int k = 0; k < lines.Count; k++) {
                        string str = lines[k];
                        writer.Write(str);
                    }
                    writer.Write(";\n");

                    writer.WriteLine($"    }}");
                    return true;
                } else if (var.GetBlock != null || var.SetBlock != null) {
                    if (var.GetBlock != null) {
                        ConversionFunction fn = new ConversionFunction();
                        fn.Name = $"get_{var.Name}";

                        fn.RawBlock = var.GetBlock;

                        writer.WriteLine($"    {access}{isStatic} get {var.Name}(): {type} {{");
                        fn.WriteLines(conversion, program, cl, writer);
                        writer.WriteLine();
                        writer.WriteLine($"    }}");
                    }

                    if (var.SetBlock != null) {
                        ConversionFunction fn = new ConversionFunction();
                        ConversionVariable value = new ConversionVariable();
                        value.VarType = var.VarType;
                        value.Name = "value";
                        fn.InParameters.Add(value);
                        fn.Name = $"set_{var.Name}";

                        fn.RawBlock = var.SetBlock;

                        writer.WriteLine($"    {access}{isStatic} set {var.Name}(value: {type}) {{");
                        fn.WriteLines(conversion, program, cl, writer);
                        writer.WriteLine();
                        writer.WriteLine($"    }}");
                    }
                } else {
                    writer.WriteLine($"    {access}{accessType}{isStatic} {var.Name}{definiteAssignment}: {type}{assignment};");
                }
            }

            return false;
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

                if (cl.Name == "ApplicationManager" && fn.Name == "BootApplication") {
                    //Debugger.Break();
                }

                fn.WriteLines(conversion, program, cl);
            }
        }

        /// <summary>
        /// Emits TypeScript declarations for a conversion class.
        /// </summary>
        /// <param name="cl">The class to emit.</param>
        /// <param name="writer">The writer receiving the output.</param>
        void writeClass(ConversionClass cl, StreamWriter writer) {
            if (cl.IsNative) {
                return;
            }

            INamedTypeSymbol typeSymbol = null;
            if (cl.TypeSymbol is INamedTypeSymbol namedTypeSymbol) {
                typeSymbol = namedTypeSymbol;
            }

            bool emitReflection = conversionOptions.Reflection.EnableReflection && typeSymbol != null;
            bool emitStaticReflection = emitReflection && conversionOptions.Reflection.UseStaticReflectionCache;
            bool emitTrailingReflection = emitReflection && !conversionOptions.Reflection.UseStaticReflectionCache;

            string implements;
            string extends;
            TypeScriptUtils.GetInheritance(program, cl, out implements, out extends);

            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                writer.WriteLine($"export interface {cl.Name}{extends} {{");
            } else if (cl.DeclarationType == MemberDeclarationType.Abstract) {
                writer.WriteLine($"export abstract class {cl.Name}{extends} {{");
            } else if (cl.DeclarationType == MemberDeclarationType.Delegate) {
                ConversionFunction del = cl.Functions[0];

                string generic = del.GetGenericArguments();

                writer.Write($"export type {del.Remap}{generic} = (");

                for (int k = 0; k < del.InParameters.Count; k++) {
                    var param = del.InParameters[k];
                    string type = param.VarType.ToTypeScriptString(tsProgram);
                    string def = param.DefaultValue;
                    if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                        def = "";
                    }

                    writer.Write($"{param.Name}: {type}{def}");

                    if (k != del.InParameters.Count - 1) {
                        writer.Write($", ");
                    }
                }

                writer.Write(") => ");

                if (del.ReturnType == null) {
                    writer.Write("void");
                } else {
                    writer.Write(del.ReturnType.ToTypeScriptString(tsProgram));
                }

                writer.WriteLine(";");
                if (emitReflection && typeSymbol != null) {
                    writer.WriteLine();
                    TypeScriptReflectionEmitter.EmitInterfaceNamespaceReflection(writer, typeSymbol, del.Remap, conversionOptions.Reflection);
                    needsReflectionMetadataImport = true;
                }
                writer.WriteLine();

                return;
            } else if (cl.DeclarationType == MemberDeclarationType.Enum) {
                writer.WriteLine($"export enum {cl.Name} {{");
                if (cl.EnumMembers != null) {
                    for (int j = 0; j < cl.EnumMembers.Count; j++) {
                        if (j == cl.EnumMembers.Count - 1) {
                            writer.WriteLine($"    {cl.EnumMembers[j]}");
                        } else {
                            writer.WriteLine($"    {cl.EnumMembers[j]},");
                        }
                    }
                }
                writer.WriteLine($"}}");
                if (emitReflection && typeSymbol != null) {
                    TypeScriptReflectionEmitter.EmitEnumNamespaceReflection(writer, typeSymbol, cl.Name, conversionOptions.Reflection);
                    needsReflectionEnumImport = true;
                }
                writer.WriteLine();

                return;
            } else {
                if (cl.GenericArgs != null && cl.GenericArgs.Count > 0) {
                    string generics = "";
                    for (int j = 0; j < cl.GenericArgs.Count; j++) {
                        generics += cl.GenericArgs[j];
                        if (j != cl.GenericArgs.Count - 1) {
                            generics += ", ";
                        }
                    }

                    writer.WriteLine($"export class {cl.Name}<{generics}>{extends}{implements} {{");
                } else {
                    writer.WriteLine($"export class {cl.Name}{extends}{implements} {{");
                }
            }

            SortVariables(cl);

            for (int j = 0; j < cl.Variables.Count; j++) {
                ConversionVariable var = cl.Variables[j];
                if (writeVariable(cl, var, writer)) {
                    if (j != cl.Variables.Count - 1) {
                        writer.WriteLine();
                    }
                }
            }

            if (cl.Variables.Count > 0) {
                writer.WriteLine();
            }

            if (emitStaticReflection && cl.DeclarationType != MemberDeclarationType.Interface && cl.DeclarationType != MemberDeclarationType.Delegate && cl.DeclarationType != MemberDeclarationType.Enum) {
                TypeScriptReflectionEmitter.EmitPrivateStaticReflectionField(writer, typeSymbol, cl.Name, conversionOptions.Reflection);
                Console.WriteLine($"-- reflection cache field emitted for {cl.Name} ({cl.DeclarationType})");
                needsReflectionTypeImport = true;
                writer.WriteLine();
            }

            writeStaticConstructor(cl, writer);

            writeConstructors(cl, writer);

            var functions = cl.Functions.Where(c => !c.IsConstructor).ToList();

            for (int j = 0; j < functions.Count; j++) {
                ConversionFunction fn = functions[j];
                string name = fn.Name;

                string access = fn.AccessType.ToString().ToLowerInvariant();
                if (cl.DeclarationType == MemberDeclarationType.Class) {
                    access += " ";
                } else {
                    access = "";
                }

                if (j != 0) {
                    writer.WriteLine();
                }

                if (cl.Name == "WebSocketClientMessageHandler" && name == "CreateWs") {
                    //Debugger.Break();
                }

                List<string> lines = fn.WriteLines(conversion, program, cl);

                string generic = fn.GetGenericArguments();
                string clType = fn.GetClassType();
                string async = fn.GetAsync();

                if (cl.DeclarationType == MemberDeclarationType.Interface) {
                    async = "";
                }

                if (fn.IsStatic) {
                    writer.Write($"    {access}static {async}{fn.Remap}{generic}(");
                } else {
                    writer.Write($"    {access}{clType}{async}{fn.Remap}{generic}(");
                }

                for (int k = 0; k < fn.InParameters.Count; k++) {
                    var param = fn.InParameters[k];
                    string type = param.VarType.ToTypeScriptString(tsProgram);
                    string def = param.DefaultValue;
                    if (string.IsNullOrEmpty(def) || cl.DeclarationType != MemberDeclarationType.Class) {
                        def = "";
                    }

                    if (param.Modifier == core.ParameterModifier.Out) {
                        writer.Write($"{param.Name}: {{ value: {type}{def} }}");
                    } else {
                        writer.Write($"{param.Name}: {type}{def}");
                    }

                    if (k != fn.InParameters.Count - 1) {
                        writer.Write($", ");
                    }
                }

                string returnParameter = null;
                if (fn.ReturnType != null) {
                    returnParameter = fn.ReturnType.ToTypeScriptString(tsProgram);
                }
                if (string.IsNullOrWhiteSpace(returnParameter)) {
                    returnParameter = "void";
                }
                if (fn.IsAsync) {
                    returnParameter = returnParameter == "void"
                        ? "Promise<void>"
                        : $"Promise<{returnParameter}>";
                }

                if (cl.DeclarationType == MemberDeclarationType.Interface || !fn.HasBody) {
                    writer.WriteLine($"): {returnParameter};");
                } else {
                    writer.WriteLine($"): {returnParameter} {{");

                    TypeScriptFunction.PrintLines(writer, lines);
                    writer.WriteLine("    }");
                }

            }

            writer.WriteLine($"}}");
            if (cl.DeclarationType == MemberDeclarationType.Interface) {
                if (emitReflection && typeSymbol != null) {
                    TypeScriptReflectionEmitter.EmitInterfaceNamespaceReflection(writer, typeSymbol, cl.Name, conversionOptions.Reflection);
                    needsReflectionMetadataImport = true;
                }
            } else if (emitTrailingReflection && typeSymbol != null) {
                TypeScriptReflectionEmitter.EmitRegisterForType(writer, typeSymbol, cl.Name, conversionOptions.Reflection.RegisterTypeIdent);
                needsReflectionTypeImport = true;
            }
            writer.WriteLine();
        }

        /// <summary>
        /// Computes which reflection runtime imports are required for the output file.
        /// </summary>
        void ComputeReflectionImports() {
            needsReflectionTypeImport = false;
            needsReflectionEnumImport = false;
            needsReflectionMetadataImport = false;

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
                        needsReflectionEnumImport = true;
                        break;
                    case MemberDeclarationType.Interface:
                        needsReflectionMetadataImport = true;
                        break;
                    case MemberDeclarationType.Delegate:
                        needsReflectionMetadataImport = true;
                        break;
                    default:
                        needsReflectionTypeImport = true;
                        break;
                }

                if (needsReflectionTypeImport && needsReflectionEnumImport && needsReflectionMetadataImport) {
                    break;
                }
            }
        }

        /// <summary>
        /// Writes the full TypeScript output for the current program.
        /// </summary>
        /// <param name="writer">The writer receiving the output.</param>
        void writeOutput(StreamWriter writer) {
            SortProgram();

            // pre-process
            int steps = 3;
            for (int i = 0; i < steps; i++) {
                for (int j = 0; j < program.Classes.Count; j++) {
                    preprocessClass(program.Classes[j]);
                }
            }

            // check for async functions in inheritance hierarchy
            for (int j = 0; j < program.Classes.Count; j++) {
                ConversionClass cl = program.Classes[j];
                for (int i = 0; i < cl.Functions.Count; i++) {
                    ConversionFunction fn = cl.Functions[i];

                    if (fn.IsAsync) {
                        // check parent and child classes for the same function name
                        // and make them async too
                        string functionName = fn.Name;

                        // Check all classes that might have this function (parent or child)
                        for (int k = 0; k < program.Classes.Count; k++) {
                            ConversionClass otherClass = program.Classes[k];

                            // Skip the current class
                            if (otherClass == cl) continue;

                            // Check if this class extends the current class or vice versa
                            bool isRelated = cl.Extensions.Contains(otherClass.Name) ||
                                           otherClass.Extensions.Contains(cl.Name);

                            if (isRelated) {
                                // Find the same function in the related class
                                ConversionFunction relatedFn = otherClass.Functions.FirstOrDefault(f => f.Name == functionName);
                                if (relatedFn != null) {
                                    // Make the related function async too
                                    relatedFn.IsAsync = true;
                                }
                            }
                        }
                    }
                }
            }

            // Also check for base class async functions that need to propagate to derived classes
            for (int j = 0; j < program.Classes.Count; j++) {
                ConversionClass cl = program.Classes[j];
                for (int i = 0; i < cl.Functions.Count; i++) {
                    ConversionFunction fn = cl.Functions[i];

                    if (fn.IsAsync) {
                        string functionName = fn.Name;

                        // Find all classes that extend this class (derived classes)
                        for (int k = 0; k < program.Classes.Count; k++) {
                            ConversionClass derivedClass = program.Classes[k];

                            // Skip the current class
                            if (derivedClass == cl) continue;

                            // Check if derived class extends this class
                            if (derivedClass.Extensions.Contains(cl.Name)) {
                                // Find the same function in the derived class
                                ConversionFunction derivedFn = derivedClass.Functions.FirstOrDefault(f => f.Name == functionName);
                                if (derivedFn != null) {
                                    // Make the derived function async too
                                    derivedFn.IsAsync = true;
                                }
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < program.Classes.Count; i++) {
                writeClass(program.Classes[i], writer);
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

                string extractor = Path.Combine(runtimeDir, "extractor.js");
                if (!File.Exists(extractor)) {
                    Console.WriteLine("Warning: extractor.js not found; metadata may be stale.");
                    return;
                }

                string typesDir = Path.Combine(runtimeDir, "node_modules", "typescript");
                if (!Directory.Exists(typesDir)) {
                    string packageJson = Path.Combine(runtimeDir, "package.json");
                    if (!File.Exists(packageJson)) {
                        Console.WriteLine("Metadata not regenerated: package.json not found in .net.ts.");
                        return;
                    }

                    Console.WriteLine("-- npm install");
                    var npmInstall = OperatingSystem.IsWindows()
                        ? new ProcessStartInfo("cmd.exe", "/c npm install")
                        : new ProcessStartInfo("npm", "install");
                    npmInstall.WorkingDirectory = runtimeDir;
                    npmInstall.UseShellExecute = false;
                    npmInstall.CreateNoWindow = true;
                    npmInstall.RedirectStandardOutput = true;
                    npmInstall.RedirectStandardError = true;

                    using (var npmProcess = Process.Start(npmInstall)) {
                        if (npmProcess == null) {
                            Console.WriteLine("Warning: failed to launch npm install (npm not found?).");
                            return;
                        }

                        npmProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine(e.Data); };
                        npmProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine(e.Data); };
                        npmProcess.BeginOutputReadLine();
                        npmProcess.BeginErrorReadLine();

                        if (!npmProcess.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds)) {
                            Console.WriteLine("Warning: npm install timed out after 3 minutes.");
                            try { npmProcess.Kill(); } catch { }
                            return;
                        }

                        npmProcess.WaitForExit();
                        if (npmProcess.ExitCode != 0) {
                            Console.WriteLine($"Warning: npm install exited with code {npmProcess.ExitCode}.");
                            return;
                        }
                    }
                }

                var psi = new ProcessStartInfo {
                    FileName = "node",
                    Arguments = $"\"{extractor}\" \"{runtimeDir}\"",
                    WorkingDirectory = runtimeDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) {
                    Console.WriteLine("Warning: failed to launch metadata extractor (node not found?).");
                    return;
                }
                process.WaitForExit();
                if (process.ExitCode != 0) {
                    Console.WriteLine($"Warning: metadata extractor exited with code {process.ExitCode}.");
                }
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
