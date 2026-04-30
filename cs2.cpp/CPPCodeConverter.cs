using cs2.core;
using cs2.core.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using System.Reflection;

namespace cs2.cpp {
    public class CPPCodeConverter : CodeConverter {
        string assemblyName;
        string version;
        string targetFramework;
        readonly string[] preprocessorSymbols;
        readonly bool includeProjectPreprocessorSymbols;

        CPPConversiorProcessor conversion;
        CPPProgram tsProgram;
        readonly CPPClassEmitter classEmitter;
        public CPPConversionRules CPPRules { get; private set; }
        public CPPConversionOptions Options { get; private set; }
        public CPPConversionReport Report { get; private set; }
        public CPPBuildUsageReport BuildUsageReport { get; private set; }
        public CPPRuntimeRequirementCatalog RuntimeRequirementCatalog { get; private set; }
        public CPPRuntimeRequirementRegistrar RuntimeRequirementRegistrar { get; private set; }

        protected override string[] PreProcessorSymbols { get { return preprocessorSymbols; } }
        internal bool IncludeProjectPreprocessorSymbols => includeProjectPreprocessorSymbols;
        internal string[] PreprocessorSymbols => preprocessorSymbols;

        public CPPCodeConverter(CPPConversionRules rules, CPPConversionOptions options = null)
            : base(rules) {
            this.CPPRules = rules;
            Options = options ?? CPPConversionOptions.CreateDefault();
            preprocessorSymbols = BuildPreprocessorSymbols(Options);
            includeProjectPreprocessorSymbols = Options.IncludeProjectDefinedPreprocessorSymbols;
            Report = new CPPConversionReport();
            BuildUsageReport = new CPPBuildUsageReport();
            RuntimeRequirementCatalog = new CPPRuntimeRequirementCatalog();
            RuntimeRequirementRegistrar = new CPPRuntimeRequirementRegistrar(RuntimeRequirementCatalog, Report);

            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

            workspace = MSBuildWorkspace.Create();

            tsProgram = new CPPProgram(rules);
            program = tsProgram;

            context = new ConversionContext(program);

            conversion = new CPPConversiorProcessor(this);
            classEmitter = new CPPClassEmitter(conversion, tsProgram);

            if (Options.LoadNativeRuntimeMetadata) {
                tsProgram.AddDotNet();
            }

            assemblyName = "";
            version = "";
            targetFramework = "";

            ResetRunState();
        }

        /// <summary>
        /// Builds the active set of preprocessor symbols for the C++ backend.
        /// </summary>
        /// <param name="options">The conversion options that may contribute additional symbols.</param>
        /// <returns>The resolved preprocessor symbol list.</returns>
        static string[] BuildPreprocessorSymbols(CPPConversionOptions options) {
            HashSet<string> symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "CPP",
                "CSHARP"
            };

            if (options?.AdditionalPreprocessorSymbols != null) {
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
        /// Configures the conversion pipeline with the C++-specific state and metadata stages.
        /// </summary>
        /// <param name="builder">The pipeline builder used to register conversion stages.</param>
        protected override void ConfigurePipeline(ConversionPipelineBuilder builder) {
            if (builder == null) {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddStage(new CPPResetConversionStateStage(this))
                   .AddStage(new ApplyPreprocessorSymbolsStage(PreProcessorSymbols))
                   .AddStage(new CPPPreprocessorFilterStage(this))
                   .AddStage(new CPPAssemblyMetadataStage(this))
                   .AddStage(new DocumentPreprocessingStage())
                   .AddStage(new ClassProcessingStage())
                   .AddStage(new ProgramSortingStage());
        }

        public void WriteOutput(string outputFolder) {
            var replacements = new Dictionary<string, string>() {
                { "ASSEMBLY_NAME", assemblyName },
                { "ASSEMBLY_VERSION", version },
                { "ASSEMBLY_DESCRIPTION", targetFramework }
            };

            if (Directory.Exists(outputFolder)) {
                //Directory.Delete(outputFolder, true);
            }

            Directory.CreateDirectory(outputFolder);

            BuildUsageReport = ResolveBuildUsageReport();
            Report.BuildUsageReport = BuildUsageReport;
            RuntimeRequirementRegistrar.ApplyBuildUsageReport(BuildUsageReport);

            string rootPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, ".net.cpp");
            CopyRuntimeFiles(new DirectoryInfo(rootPath), new DirectoryInfo(outputFolder), replacements);

            writeClasses(outputFolder, BuildUsageReport);
            PruneDisabledFeatureRuntimeFiles(outputFolder);

            string configPath = CPPGeneratedConfigWriter.Write(outputFolder, Options, RuntimeRequirementRegistrar, BuildUsageReport);
            TrackEmittedFile(configPath);

            foreach (string manifestFile in CPPFeatureManifestWriter.Write(outputFolder, BuildUsageReport)) {
                TrackEmittedFile(manifestFile);
            }

            foreach (string harnessFile in CPPCompileHarnessWriter.Write(outputFolder, Options)) {
                TrackEmittedFile(harnessFile);
            }

            string windowsHandoffPath = CPPWindowsHandoffWriter.Write(outputFolder);
            TrackEmittedFile(windowsHandoffPath);

            if (Options.WriteConversionReport) {
                string reportPath = Path.Combine(outputFolder, CPPConversionReportWriter.DefaultFileName);
                TrackEmittedFile(reportPath);
                CPPConversionReportWriter.Write(outputFolder, Report, Options);
            }

            MirrorWindowsHandoffOutput(outputFolder);

            //string outputFile = Path.Combine(outputFolder, fileName);
            //string outputDir = Path.GetDirectoryName(outputFile)!;
            //Directory.CreateDirectory(outputDir);

            //Stream stream = File.OpenWrite(outputFile);
            //StreamWriter writer = new StreamWriter(stream);

            //foreach (var pair in program.Requirements) {
            //    if (pair is GenericKnownClass gen) {
            //        string imports = "";

            //        for (int i = gen.Start; i < gen.TotalImports; i++) {
            //            if (i == gen.TotalImports - 1) {
            //                imports += gen.Name + i;
            //            } else if (i == gen.Start) {
            //                imports += gen.Name + ", ";
            //            } else {
            //                imports += gen.Name + i + ", ";
            //            }
            //        }

            //        writer.WriteLine($"import type {{ {imports} }} from \"{pair.Path}\";");
            //    } else {
            //        if (string.IsNullOrEmpty(pair.Replacement)) {
            //            writer.WriteLine($"import {{ {pair.Name} }} from \"{pair.Path}\";");
            //        } else {
            //            writer.WriteLine($"import {{ {pair.Name} as {pair.Replacement} }} from \"{pair.Path}\";");
            //        }
            //    }
            //}

            //writeOutput(writer);
        }

        /// <summary>
        /// Registers a named runtime requirement for the active conversion run.
        /// </summary>
        /// <param name="name">The stable runtime requirement name.</param>
        public void RegisterRuntimeRequirement(string name) {
            RuntimeRequirementRegistrar.Register(name);
            SynchronizeRunState();
        }

        /// <summary>
        /// Records a structured unsupported-construct diagnostic for the active conversion run.
        /// </summary>
        /// <param name="sourceTypeName">The source type that contains the unsupported construct.</param>
        /// <param name="sourceMemberName">The source member that contains the unsupported construct.</param>
        /// <param name="syntaxKind">The Roslyn syntax kind that could not be lowered.</param>
        /// <param name="message">Human-readable explanation of why the construct is unsupported.</param>
        /// <param name="recommendation">Suggested next action to make the code portable.</param>
        /// <param name="filePath">The source file path when available.</param>
        public void ReportUnsupportedConstruct(string sourceTypeName, string sourceMemberName, string syntaxKind, string message, string recommendation, string filePath = "") {
            if (string.IsNullOrWhiteSpace(message)) {
                throw new ArgumentException("Unsupported construct message must not be empty.", nameof(message));
            }

            Report.AddDiagnostic(CPPDiagnosticSeverity.Error, "CPP1000", message);
            CPPConversionDiagnostic diagnostic = Report.Diagnostics[^1];
            diagnostic.SourceTypeName = sourceTypeName ?? string.Empty;
            diagnostic.SourceMemberName = sourceMemberName ?? string.Empty;
            diagnostic.SyntaxKind = syntaxKind ?? string.Empty;
            diagnostic.Recommendation = recommendation ?? string.Empty;
            diagnostic.FilePath = filePath ?? string.Empty;
            SynchronizeRunState();
        }

        /// <summary>
        /// Resets all per-run converter state so repeated conversions start from a deterministic baseline.
        /// </summary>
        public void ResetRunState() {
            assemblyName = string.Empty;
            version = string.Empty;
            targetFramework = string.Empty;

            Report.Reset();
            BuildUsageReport = new CPPBuildUsageReport();
            Report.BuildUsageReport = BuildUsageReport;
            RuntimeRequirementRegistrar.Reset();
            RuntimeRequirementRegistrar.ApplyBuildUsageReport(BuildUsageReport);
            RuntimeRequirementRegistrar.RegisterDefaults(Options);
            SynchronizeRunState();
        }

        /// <summary>
        /// Applies resolved assembly metadata to the converter and current conversion report.
        /// </summary>
        /// <param name="assembly">The resolved assembly name.</param>
        /// <param name="assemblyVersion">The resolved assembly version.</param>
        /// <param name="framework">The resolved target framework.</param>
        internal void SetAssemblyMetadata(string assembly, string assemblyVersion, string framework) {
            assemblyName = assembly ?? string.Empty;
            version = assemblyVersion ?? string.Empty;
            targetFramework = framework ?? string.Empty;

            Report.AssemblyName = assemblyName;
            Report.AssemblyVersion = version;
            Report.TargetFramework = targetFramework;
            SynchronizeRunState();
        }

        /// <summary>
        /// Copies runtime support files into the generated output folder.
        /// </summary>
        /// <param name="sourceDirectory">Directory that contains the runtime template files.</param>
        /// <param name="targetDirectory">Directory that receives the generated runtime files.</param>
        /// <param name="replacements">Placeholder replacements applied while copying text files.</param>
        private static void CopyRuntimeFiles(DirectoryInfo sourceDirectory, DirectoryInfo targetDirectory, IReadOnlyDictionary<string, string> replacements) {
            if (!sourceDirectory.Exists) {
                throw new DirectoryNotFoundException($"Runtime template directory '{sourceDirectory.FullName}' was not found.");
            }

            CopyDirectoryRecursively(sourceDirectory, targetDirectory, replacements);
        }

        /// <summary>
        /// Recursively copies a directory tree while applying placeholder replacements to non-JSON files.
        /// </summary>
        /// <param name="sourceDirectory">Directory to copy from.</param>
        /// <param name="targetDirectory">Directory to copy into.</param>
        /// <param name="replacements">Placeholder replacements applied to copied file contents.</param>
        private static void CopyDirectoryRecursively(DirectoryInfo sourceDirectory, DirectoryInfo targetDirectory, IReadOnlyDictionary<string, string> replacements) {
            Directory.CreateDirectory(targetDirectory.FullName);

            foreach (DirectoryInfo childDirectory in sourceDirectory.GetDirectories()) {
                DirectoryInfo childTargetDirectory = new DirectoryInfo(Path.Combine(targetDirectory.FullName, childDirectory.Name));
                CopyDirectoryRecursively(childDirectory, childTargetDirectory, replacements);
            }

            foreach (FileInfo sourceFile in sourceDirectory.GetFiles()) {
                if (sourceFile.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                string source = File.ReadAllText(sourceFile.FullName);
                source = Utils.ReplacePlaceholders(source, replacements.ToDictionary(pair => pair.Key, pair => pair.Value));

                string targetPath = Path.Combine(targetDirectory.FullName, sourceFile.Name);
                File.WriteAllText(targetPath, source);
            }
        }

        /// <summary>
        /// Removes feature-owned runtime helper files that were not registered for the resolved build.
        /// </summary>
        /// <param name="outputFolder">Generated output folder that contains copied runtime templates.</param>
        void PruneDisabledFeatureRuntimeFiles(string outputFolder) {
            foreach (CPPRuntimeRequirementDefinition definition in RuntimeRequirementCatalog.Definitions) {
                if (definition.OwningFeatures.Count == 0) {
                    continue;
                }

                if (RuntimeRequirementRegistrar.IsRegistered(definition.Name)) {
                    continue;
                }

                string runtimePath = Path.Combine(outputFolder, definition.IncludePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(runtimePath)) {
                    File.Delete(runtimePath);
                }
            }
        }

        /// <summary>
        /// Mirrors generated output into the configured Windows handoff folder when the conversion options request it.
        /// </summary>
        /// <param name="outputFolder">Generated output folder that contains the fresh core files.</param>
        void MirrorWindowsHandoffOutput(string outputFolder) {
            if (string.IsNullOrWhiteSpace(Options.WindowsHandoffOutputFolder)) {
                return;
            }

            string sourcePath = Path.GetFullPath(outputFolder);
            string handoffPath = Path.GetFullPath(Options.WindowsHandoffOutputFolder);

            if (string.Equals(sourcePath, handoffPath, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            if (Directory.Exists(handoffPath)) {
                Directory.Delete(handoffPath, true);
            }

            CopyGeneratedOutputRecursively(new DirectoryInfo(sourcePath), new DirectoryInfo(handoffPath));
        }

        /// <summary>
        /// Copies fully generated output files into a handoff folder without reapplying template replacements.
        /// </summary>
        /// <param name="sourceDirectory">Directory that contains the completed generated output.</param>
        /// <param name="targetDirectory">Directory that receives the mirrored handoff copy.</param>
        static void CopyGeneratedOutputRecursively(DirectoryInfo sourceDirectory, DirectoryInfo targetDirectory) {
            Directory.CreateDirectory(targetDirectory.FullName);

            foreach (DirectoryInfo childDirectory in sourceDirectory.GetDirectories()) {
                DirectoryInfo childTargetDirectory = new DirectoryInfo(Path.Combine(targetDirectory.FullName, childDirectory.Name));
                CopyGeneratedOutputRecursively(childDirectory, childTargetDirectory);
            }

            foreach (FileInfo sourceFile in sourceDirectory.GetFiles()) {
                sourceFile.CopyTo(Path.Combine(targetDirectory.FullName, sourceFile.Name), true);
            }
        }

        private void writeClasses(string folder, CPPBuildUsageReport buildUsageReport) {
            SortProgram();
            CPPReachabilityPlan reachabilityPlan = CPPReachabilityPlanner.Build(program, buildUsageReport);

            for (int i = 0; i < reachabilityPlan.Types.Count; i++) {
                ConversionClass cl = reachabilityPlan.Types[i];
                if (cl.IsNative) {
                    continue;
                }

                string filePath = Path.Combine(folder, cl.GetEmittedTypeName());
                string headerPath = filePath + ".hpp";
                string codePath = filePath + ".cpp";

                using (StreamWriter writerHeader = new StreamWriter(headerPath)) {
                    using (StreamWriter writerCode = new StreamWriter(codePath)) {
                        SortVariables(cl);
                        SortFunctions(cl);
                        classEmitter.Emit(cl, writerHeader, writerCode);

                        writerCode.Flush();
                        writerHeader.Flush();
                    }
                }

                TrackEmittedFile(headerPath);
                TrackEmittedFile(codePath);
                Report.EmittedTypeCount++;
            }
        }

        /// <summary>
        /// Resolves the feature usage report for the currently loaded Roslyn project.
        /// </summary>
        /// <returns>The resolved build usage report for the current conversion.</returns>
        CPPBuildUsageReport ResolveBuildUsageReport() {
            if (project == null) {
                return CPPFeatureResolver.Resolve(Options.BuildFeatureProfile, Array.Empty<CPPFeatureUsageRoot>());
            }

            Compilation compilation = AsyncUtil.RunSync(() => project.GetCompilationAsync());
            if (compilation is not CSharpCompilation csharpCompilation) {
                return CPPFeatureResolver.Resolve(Options.BuildFeatureProfile, Array.Empty<CPPFeatureUsageRoot>());
            }

            IReadOnlyList<CPPFeatureUsageRoot> detectedRoots = CPPFeatureScanner.Scan(csharpCompilation);
            return CPPFeatureResolver.Resolve(Options.BuildFeatureProfile, detectedRoots);
        }

        protected override void PreProcessExpression(SemanticModel model, MemberDeclarationSyntax member, ConversionContext context) {
            ConversionPreProcessor.PreProcessExpression(model, context, member);
        }

        protected override void ProcessClass(ConversionClass cl, ConversionProgram program) {
            conversion.ProcessClass(cl, program);
        }

        /// <summary>
        /// Records a generated file path in the active conversion report and program mirror state.
        /// </summary>
        /// <param name="filePath">The generated file path to track.</param>
        void TrackEmittedFile(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                throw new ArgumentException("Generated file path must not be empty.", nameof(filePath));
            }

            if (!Report.EmittedFiles.Contains(filePath, StringComparer.Ordinal)) {
                Report.EmittedFiles.Add(filePath);
            }

            SynchronizeRunState();
        }

        /// <summary>
        /// Mirrors the current converter options, runtime requirements, and report state into the C++ program model.
        /// </summary>
        void SynchronizeRunState() {
            tsProgram.Options = Options;
            tsProgram.Report = Report;

            tsProgram.EmittedFiles.Clear();
            tsProgram.EmittedFiles.AddRange(Report.EmittedFiles);

            tsProgram.RuntimeRequirements.Clear();
            Report.RegisteredRuntimeRequirements.Clear();

            foreach (CPPRuntimeRequirementDefinition requirement in RuntimeRequirementRegistrar.RegisteredRequirements.OrderBy(requirement => requirement.Name, StringComparer.Ordinal)) {
                tsProgram.RuntimeRequirements.Add(requirement.Name);
                Report.RegisteredRuntimeRequirements.Add(requirement.Name);
            }
        }
    }
}
