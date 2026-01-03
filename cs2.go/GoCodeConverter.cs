using cs2.core;
using cs2.core.Pipeline;
using cs2.go.pipeline;
using cs2.go.util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Nucleus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace cs2.go {
    /// <summary>
    /// Converts C# code to Go using Roslyn and GoProgram metadata.
    /// </summary>
    public class GoCodeConverter : CodeConverter {
        /// <summary>
        /// Stores the resolved assembly name for placeholder replacement.
        /// </summary>
        string AssemblyName;
        /// <summary>
        /// Stores the resolved assembly version for placeholder replacement.
        /// </summary>
        string Version;
        /// <summary>
        /// Stores the resolved target framework for placeholder replacement.
        /// </summary>
        string TargetFramework;

        /// <summary>
        /// Handles Go-specific conversion of syntax nodes.
        /// </summary>
        GoConversiorProcessor Conversion;
        /// <summary>
        /// Holds the Go conversion program metadata.
        /// </summary>
        GoProgram GoProgram;
        /// <summary>
        /// Stores the resolved conversion options used during conversion.
        /// </summary>
        readonly GoConversionOptions ConversionOptions;
        /// <summary>
        /// Stores the preprocessor symbols used for conversion.
        /// </summary>
        readonly string[] PreprocessorSymbolsInternal;
        /// <summary>
        /// Indicates whether project-defined preprocessor symbols are retained.
        /// </summary>
        readonly bool IncludeProjectPreprocessorSymbolsInternal;
        /// <summary>
        /// Tracks Go import requirements.
        /// </summary>
        readonly GoImportTracker ImportTracker;
        /// <summary>
        /// Tracks helper usage for generated output.
        /// </summary>
        readonly GoHelperUsage HelperUsage;
        /// <summary>
        /// Emits Go structs, interfaces, and enums.
        /// </summary>
        GoClassEmitter ClassEmitter;

        /// <summary>
        /// Creates a new converter with the given options.
        /// </summary>
        /// <param name="rules">The conversion rules shared across backends.</param>
        /// <param name="options">Optional conversion options.</param>
        public GoCodeConverter(ConversionRules rules, GoConversionOptions options = null)
            : base(rules) {
            ConversionOptions = options == null ? GoConversionOptions.Default.Clone() : options.Clone();
            PreprocessorSymbolsInternal = BuildPreprocessorSymbols(ConversionOptions);
            IncludeProjectPreprocessorSymbolsInternal = ConversionOptions.IncludeProjectDefinedPreprocessorSymbols;

            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

            workspace = MSBuildWorkspace.Create();

            GoProgram = new GoProgram(rules);
            program = GoProgram;

            GoProgram.AddDotNet();
            context = new ConversionContext(program);

            ImportTracker = new GoImportTracker();
            HelperUsage = new GoHelperUsage();
            Conversion = new GoConversiorProcessor(GoProgram, ImportTracker, HelperUsage);
            ClassEmitter = new GoClassEmitter(Conversion, program, GoProgram, ImportTracker, ConversionOptions, SortVariables);

            AssemblyName = string.Empty;
            Version = string.Empty;
            TargetFramework = string.Empty;
        }

        /// <summary>
        /// Gets the preprocessor symbols applied during conversion.
        /// </summary>
        protected override string[] PreProcessorSymbols => PreprocessorSymbolsInternal;

        /// <summary>
        /// Gets whether project-defined preprocessor symbols are preserved.
        /// </summary>
        internal bool IncludeProjectPreprocessorSymbols => IncludeProjectPreprocessorSymbolsInternal;

        /// <summary>
        /// Gets the internal preprocessor symbol list for pipeline stages.
        /// </summary>
        internal string[] PreprocessorSymbols => PreprocessorSymbolsInternal;

        /// <summary>
        /// Builds the full set of preprocessor symbols used during conversion.
        /// </summary>
        /// <param name="options">The conversion options that may add symbols.</param>
        /// <returns>The resolved preprocessor symbol list.</returns>
        static string[] BuildPreprocessorSymbols(GoConversionOptions options) {
            HashSet<string> symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GO", "CSHARP" };

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
        /// Configures the conversion pipeline with Go-specific stages.
        /// </summary>
        /// <param name="builder">The pipeline builder used to register stages.</param>
        protected override void ConfigurePipeline(ConversionPipelineBuilder builder) {
            base.ConfigurePipeline(builder);

            var preprocessorFilter = new GoPreprocessorFilterStage(this);
            var metadataStage = new GoAssemblyMetadataStage(this);

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
        /// Writes the generated Go file into the output folder.
        /// </summary>
        /// <param name="outputFolder">The folder that receives generated output.</param>
        /// <param name="fileName">The Go file name to write.</param>
        public void WriteFile(string outputFolder, string fileName) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder cannot be empty.", nameof(outputFolder));
            }

            if (string.IsNullOrWhiteSpace(fileName)) {
                throw new ArgumentException("Output file name cannot be empty.", nameof(fileName));
            }

            Directory.CreateDirectory(outputFolder);
            string outputFile = Path.Combine(outputFolder, fileName);

            CollectImportsAndHelpers();

            using StringWriter stringWriter = new StringWriter();
            GoOutputWriter output = new GoOutputWriter(stringWriter);

            output.WriteLine($"package {ConversionOptions.PackageName}");
            output.WriteLine();

            EmitImports(output);
            EmitHelpers(output);

            WriteOutput(output);

            File.WriteAllText(outputFile, stringWriter.ToString());
        }

        /// <summary>
        /// Stores assembly metadata for placeholder replacement in emitted assets.
        /// </summary>
        /// <param name="assembly">The resolved assembly name.</param>
        /// <param name="resolvedVersion">The resolved assembly version.</param>
        /// <param name="framework">The resolved target framework.</param>
        internal void SetAssemblyMetadata(string assembly, string resolvedVersion, string framework) {
            AssemblyName = assembly ?? string.Empty;
            Version = resolvedVersion ?? string.Empty;
            TargetFramework = framework ?? string.Empty;
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
        /// Applies Go-specific class processing after base conversion.
        /// </summary>
        /// <param name="cl">The class being processed.</param>
        /// <param name="program">The conversion program containing the class.</param>
        protected override void ProcessClass(ConversionClass cl, ConversionProgram program) {
            Conversion.ProcessClass(cl, program);
        }

        /// <summary>
        /// Collects import requirements and helper usage by preprocessing functions.
        /// </summary>
        void CollectImportsAndHelpers() {
            ImportTracker.Reset();
            HelperUsage.Reset();

            for (int i = 0; i < program.Classes.Count; i++) {
                ConversionClass cl = program.Classes[i];
                if (cl.IsNative) {
                    continue;
                }

                CollectTypeImports(cl);
                PreprocessClass(cl);
            }
        }

        /// <summary>
        /// Emits the import block if needed.
        /// </summary>
        /// <param name="writer">The output writer.</param>
        void EmitImports(GoOutputWriter writer) {
            if (!ImportTracker.HasImports()) {
                return;
            }

            writer.WriteLine("import (");
            writer.Indent();

            foreach (GoImportDefinition import in ImportTracker.CurrentImports) {
                if (string.IsNullOrWhiteSpace(import.Alias)) {
                    writer.WriteIndentedLine($"\"{import.Path}\"");
                } else {
                    writer.WriteIndentedLine($"{import.Alias} \"{import.Path}\"");
                }
            }

            writer.Outdent();
            writer.WriteLine(")");
            writer.WriteLine();
        }

        /// <summary>
        /// Emits helper functions required by the converted output.
        /// </summary>
        /// <param name="writer">The output writer.</param>
        void EmitHelpers(GoOutputWriter writer) {
            if (HelperUsage.NeedsTypeCheck) {
                writer.WriteLine("func isType[T any](value any) bool {");
                writer.WriteLine("\t_, ok := value.(T)");
                writer.WriteLine("\treturn ok");
                writer.WriteLine("}");
                writer.WriteLine();
            }

            if (!HelperUsage.NeedsTernary) {
                return;
            }

            writer.WriteLine("func ternary[T any](cond bool, whenTrue T, whenFalse T) T {");
            writer.WriteLine("\tif cond {");
            writer.WriteLine("\t\treturn whenTrue");
            writer.WriteLine("\t}");
            writer.WriteLine("\treturn whenFalse");
            writer.WriteLine("}");
            writer.WriteLine();
        }

        /// <summary>
        /// Writes the generated Go classes.
        /// </summary>
        /// <param name="writer">The writer receiving the output.</param>
        void WriteOutput(GoOutputWriter writer) {
            for (int i = 0; i < program.Classes.Count; i++) {
                ClassEmitter.EmitClass(program.Classes[i], writer);
            }
        }

        /// <summary>
        /// Preprocesses class members to collect imports and helper usage.
        /// </summary>
        /// <param name="cl">The class to preprocess.</param>
        void PreprocessClass(ConversionClass cl) {
            if (cl.IsNative) {
                return;
            }

            List<ConversionFunction> functions = cl.Functions.Where(c => !c.IsConstructor).ToList();
            for (int j = 0; j < functions.Count; j++) {
                ConversionFunction fn = functions[j];
                fn.WriteLines(Conversion, GoProgram, cl);
            }
        }

        /// <summary>
        /// Collects imports required for type usage in a class declaration.
        /// </summary>
        /// <param name="cl">The class to scan.</param>
        void CollectTypeImports(ConversionClass cl) {
            for (int i = 0; i < cl.Variables.Count; i++) {
                ConversionVariable var = cl.Variables[i];
                var.VarType.ToGoString(GoProgram, ImportTracker);
            }

            for (int i = 0; i < cl.Functions.Count; i++) {
                ConversionFunction fn = cl.Functions[i];
                if (fn.ReturnType != null) {
                    fn.ReturnType.ToGoString(GoProgram, ImportTracker);
                }

                for (int j = 0; j < fn.InParameters.Count; j++) {
                    ConversionVariable param = fn.InParameters[j];
                    param.VarType.ToGoString(GoProgram, ImportTracker);
                }
            }

            for (int i = 0; i < cl.Extensions.Count; i++) {
                TrackTypeImportByName(cl.Extensions[i]);
            }
        }

        /// <summary>
        /// Tracks import requirements for a type name.
        /// </summary>
        /// <param name="typeName">The type name to inspect.</param>
        void TrackTypeImportByName(string typeName) {
            if (string.IsNullOrWhiteSpace(typeName)) {
                return;
            }

            if (GoProgram.TypeMap.TryGetValue(typeName, out string mapped)) {
                typeName = mapped;
            }

            int dotIndex = typeName.IndexOf('.');
            if (dotIndex <= 0) {
                return;
            }

            string alias = typeName.Substring(0, dotIndex);
            if (GoProgram.TryGetPackageImport(alias, out string importPath)) {
                ImportTracker.AddImport(importPath, alias);
            }
        }
    }
}
