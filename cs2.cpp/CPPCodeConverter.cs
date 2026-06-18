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
        internal ConversionProgram Program => program;
        Compilation instantiatedGeneratedTypeCompilation;
        IReadOnlyList<INamedTypeSymbol> instantiatedGeneratedTypes;

        protected override string[] PreProcessorSymbols { get { return preprocessorSymbols; } }
        internal bool IncludeProjectPreprocessorSymbols => includeProjectPreprocessorSymbols;
        internal string[] PreprocessorSymbols => preprocessorSymbols;

        public CPPCodeConverter(CPPConversionRules rules, CPPConversionOptions options = null)
            : base(rules) {
            this.CPPRules = rules;
            Options = options ?? CPPConversionOptions.CreateDefault();
            Options = new CPPConversionPresetCatalog().ApplyTo(Options);
            preprocessorSymbols = BuildPreprocessorSymbols(Options);
            includeProjectPreprocessorSymbols = Options.IncludeProjectDefinedPreprocessorSymbols;
            Report = new CPPConversionReport();
            BuildUsageReport = new CPPBuildUsageReport();
            RuntimeRequirementCatalog = new CPPRuntimeRequirementCatalog(Options.FeatureCatalog);
            RuntimeRequirementRegistrar = new CPPRuntimeRequirementRegistrar(RuntimeRequirementCatalog, Report);

            var _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

            workspace = MSBuildWorkspace.Create();

            tsProgram = new CPPProgram(rules);
            program = tsProgram;
            PopulateConfiguredTypeMap(program, Options.TypeRemaps);

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
        /// Copies caller-provided type remaps into the shared conversion program and adds safe unique leaf-name aliases for code paths that only retain one unqualified type name.
        /// </summary>
        /// <param name="conversionProgram">Program that receives the remap table.</param>
        /// <param name="configuredTypeRemaps">Caller-provided source-to-target remaps.</param>
        static void PopulateConfiguredTypeMap(ConversionProgram conversionProgram, IReadOnlyDictionary<string, string> configuredTypeRemaps) {
            if (conversionProgram == null || configuredTypeRemaps == null || configuredTypeRemaps.Count == 0) {
                return;
            }

            Dictionary<string, int> leafCounts = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> typeRemap in configuredTypeRemaps) {
                if (string.IsNullOrWhiteSpace(typeRemap.Key) || string.IsNullOrWhiteSpace(typeRemap.Value)) {
                    continue;
                }

                conversionProgram.TypeMap[typeRemap.Key] = typeRemap.Value;
                string leafTypeName = GetLeafTypeName(typeRemap.Key);
                if (string.IsNullOrWhiteSpace(leafTypeName) || string.Equals(leafTypeName, typeRemap.Key, StringComparison.Ordinal)) {
                    continue;
                }

                if (!leafCounts.ContainsKey(leafTypeName)) {
                    leafCounts[leafTypeName] = 0;
                }

                leafCounts[leafTypeName]++;
            }

            foreach (KeyValuePair<string, string> typeRemap in configuredTypeRemaps) {
                if (string.IsNullOrWhiteSpace(typeRemap.Key) || string.IsNullOrWhiteSpace(typeRemap.Value)) {
                    continue;
                }

                string leafTypeName = GetLeafTypeName(typeRemap.Key);
                if (string.IsNullOrWhiteSpace(leafTypeName) ||
                    string.Equals(leafTypeName, typeRemap.Key, StringComparison.Ordinal) ||
                    !leafCounts.TryGetValue(leafTypeName, out int leafCount) ||
                    leafCount != 1 ||
                    conversionProgram.TypeMap.ContainsKey(leafTypeName)) {
                    continue;
                }

                conversionProgram.TypeMap[leafTypeName] = typeRemap.Value;
            }
        }

        /// <summary>
        /// Extracts the leaf type token from one caller-provided source type key.
        /// </summary>
        /// <param name="qualifiedTypeName">Qualified source type name.</param>
        /// <returns>Leaf type token without namespace or nested-type prefixes.</returns>
        static string GetLeafTypeName(string qualifiedTypeName) {
            if (string.IsNullOrWhiteSpace(qualifiedTypeName)) {
                return string.Empty;
            }

            int separatorIndex = qualifiedTypeName.LastIndexOf('.');
            if (separatorIndex >= 0 && separatorIndex < qualifiedTypeName.Length - 1) {
                return qualifiedTypeName[(separatorIndex + 1)..];
            }

            int nestedSeparatorIndex = qualifiedTypeName.LastIndexOf('+');
            if (nestedSeparatorIndex >= 0 && nestedSeparatorIndex < qualifiedTypeName.Length - 1) {
                return qualifiedTypeName[(nestedSeparatorIndex + 1)..];
            }

            return qualifiedTypeName;
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
                Directory.Delete(outputFolder, true);
            }

            Directory.CreateDirectory(outputFolder);

            BuildUsageReport = ResolveBuildUsageReport();
            Report.BuildUsageReport = BuildUsageReport;
            RuntimeRequirementRegistrar.ApplyBuildUsageReport(BuildUsageReport);
            CPPRestrictionValidationResult restrictionValidation = CPPRestrictionValidator.Validate(BuildUsageReport, RuntimeRequirementRegistrar.RegisteredRequirements, Options.RestrictionProfile);
            if (!restrictionValidation.IsValid) {
                throw new InvalidOperationException(restrictionValidation.Diagnostics[0]);
            }

            string rootPath = ResolveRuntimeTemplateDirectory();
            CopyRuntimeFiles(new DirectoryInfo(rootPath), new DirectoryInfo(outputFolder), replacements);

            writeClasses(outputFolder, BuildUsageReport);
            new CPPGeneratedOutputAdapter().Apply(outputFolder, Options);
            PruneDisabledFeatureRuntimeFiles(outputFolder);
            foreach (string supportFile in CPPGeneratedRuntimeComponentRegistrationSupportWriter.WriteIfRequired(outputFolder)) {
                TrackEmittedFile(supportFile);
            }

            string configPath = CPPGeneratedConfigWriter.Write(outputFolder, Options, RuntimeRequirementRegistrar, BuildUsageReport);
            TrackEmittedFile(configPath);

            foreach (string manifestFile in CPPFeatureManifestWriter.Write(outputFolder, BuildUsageReport)) {
                TrackEmittedFile(manifestFile);
            }

            foreach (string harnessFile in CPPCompileHarnessWriter.Write(outputFolder, Options)) {
                TrackEmittedFile(harnessFile);
            }

            CPPGeneratedOwnershipValidator.Validate(outputFolder);

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
        /// Resolves the runtime template directory used to seed generated C++ outputs, falling back from the process entry assembly to the converter assembly when tests run under testhost.
        /// </summary>
        /// <returns>The full path to the runtime template directory.</returns>
        static string ResolveRuntimeTemplateDirectory() {
            string? configuredRuntimeRoot = Environment.GetEnvironmentVariable("CS2_RUNTIME_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredRuntimeRoot)) {
                string normalizedRuntimeRoot = Path.GetFullPath(configuredRuntimeRoot);
                string configuredTemplateRoot = ResolveConfiguredRuntimeTemplateDirectory(normalizedRuntimeRoot);
                if (!string.IsNullOrWhiteSpace(configuredTemplateRoot)) {
                    return configuredTemplateRoot;
                }
            }

            List<string> candidateSearchRoots = new List<string>();

            Assembly entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null && !string.IsNullOrWhiteSpace(entryAssembly.Location)) {
                string entryAssemblyDirectory = Path.GetDirectoryName(entryAssembly.Location);
                if (!string.IsNullOrWhiteSpace(entryAssemblyDirectory)) {
                    candidateSearchRoots.Add(entryAssemblyDirectory);
                }
            }

            string converterAssemblyDirectory = Path.GetDirectoryName(typeof(CPPCodeConverter).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(converterAssemblyDirectory)) {
                candidateSearchRoots.Add(converterAssemblyDirectory);
            }

            if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory)) {
                candidateSearchRoots.Add(AppContext.BaseDirectory);
            }

            foreach (string candidateSearchRoot in candidateSearchRoots) {
                string resolvedDirectory = FindRuntimeTemplateDirectory(candidateSearchRoot);
                if (!string.IsNullOrWhiteSpace(resolvedDirectory)) {
                    return resolvedDirectory;
                }
            }

            return Path.Combine(candidateSearchRoots.FirstOrDefault() ?? AppContext.BaseDirectory, ".net.cpp");
        }

        /// <summary>
        /// Resolves one runtime template directory from a configured runtime root.
        /// </summary>
        /// <param name="runtimeRoot">Configured runtime root path.</param>
        /// <returns>The runtime template directory when found; otherwise an empty string.</returns>
        static string ResolveConfiguredRuntimeTemplateDirectory(string runtimeRoot) {
            if (string.IsNullOrWhiteSpace(runtimeRoot)) {
                return string.Empty;
            }

            if (HasRuntimeTemplateContent(runtimeRoot)) {
                return runtimeRoot;
            }

            string directRuntimeFolder = Path.Combine(runtimeRoot, ".net.cpp");
            if (HasRuntimeTemplateContent(directRuntimeFolder)) {
                return directRuntimeFolder;
            }

            string siblingRuntimeFolder = Path.Combine(runtimeRoot, "cs2.cpp", ".net.cpp");
            if (HasRuntimeTemplateContent(siblingRuntimeFolder)) {
                return siblingRuntimeFolder;
            }

            return string.Empty;
        }

        /// <summary>
        /// Walks upward from a search root until a runtime template directory with real template content is found.
        /// </summary>
        /// <param name="searchRoot">Starting directory used to probe for the runtime template folder.</param>
        /// <returns>The resolved runtime template directory when found; otherwise an empty string.</returns>
        static string FindRuntimeTemplateDirectory(string searchRoot) {
            if (string.IsNullOrWhiteSpace(searchRoot)) {
                return string.Empty;
            }

            for (DirectoryInfo currentDirectory = new DirectoryInfo(searchRoot);
                currentDirectory != null;
                currentDirectory = currentDirectory.Parent) {
                string candidateRoot = Path.Combine(currentDirectory.FullName, ".net.cpp");
                if (HasRuntimeTemplateContent(candidateRoot)) {
                    return candidateRoot;
                }

                string siblingProjectCandidateRoot = Path.Combine(currentDirectory.FullName, "cs2.cpp", ".net.cpp");
                if (HasRuntimeTemplateContent(siblingProjectCandidateRoot)) {
                    return siblingProjectCandidateRoot;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Determines whether a runtime template directory contains the expected seed files instead of an empty staging folder.
        /// </summary>
        /// <param name="candidateRoot">Directory to inspect.</param>
        /// <returns><c>true</c> when the directory contains the runtime template sentinels required for output seeding; otherwise <c>false</c>.</returns>
        static bool HasRuntimeTemplateContent(string candidateRoot) {
            if (!Directory.Exists(candidateRoot)) {
                return false;
            }

            return File.Exists(Path.Combine(candidateRoot, "system", "console.cpp")) &&
                File.Exists(Path.Combine(candidateRoot, "runtime", "native_string.hpp"));
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
            instantiatedGeneratedTypeCompilation = null;
            instantiatedGeneratedTypes = null;

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
        /// Gets the concrete generated class instantiations explicitly created in one compilation so runtime generic dispatch can target closed native types.
        /// </summary>
        /// <param name="compilation">Compilation to scan for object creation sites.</param>
        /// <returns>Distinct concrete generated types instantiated by the compilation.</returns>
        internal IReadOnlyList<INamedTypeSymbol> GetInstantiatedGeneratedTypes(Compilation compilation) {
            if (compilation == null) {
                return Array.Empty<INamedTypeSymbol>();
            }

            if (ReferenceEquals(instantiatedGeneratedTypeCompilation, compilation) && instantiatedGeneratedTypes != null) {
                return instantiatedGeneratedTypes;
            }

            List<INamedTypeSymbol> resolvedTypes = new List<INamedTypeSymbol>();
            HashSet<string> seenTypeNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees) {
                SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
                SyntaxNode root = syntaxTree.GetRoot();

                foreach (ObjectCreationExpressionSyntax objectCreation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
                    AddInstantiatedGeneratedType(semanticModel.GetTypeInfo(objectCreation).Type, resolvedTypes, seenTypeNames);
                }

                foreach (ImplicitObjectCreationExpressionSyntax objectCreation in root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>()) {
                    AddInstantiatedGeneratedType(semanticModel.GetTypeInfo(objectCreation).Type, resolvedTypes, seenTypeNames);
                }
            }

            instantiatedGeneratedTypeCompilation = compilation;
            instantiatedGeneratedTypes = resolvedTypes;
            return instantiatedGeneratedTypes;
        }

        void AddInstantiatedGeneratedType(ITypeSymbol typeSymbol, List<INamedTypeSymbol> resolvedTypes, HashSet<string> seenTypeNames) {
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol ||
                namedTypeSymbol.TypeKind != TypeKind.Class ||
                namedTypeSymbol.IsAbstract ||
                Program?.FindGeneratedClass(namedTypeSymbol) == null) {
                return;
            }

            string qualifiedTypeName = namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!seenTypeNames.Add(qualifiedTypeName)) {
                return;
            }

            resolvedTypes.Add(namedTypeSymbol);
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
                if (definition.OwningFeatureIds.Count == 0) {
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
            CPPReachabilityPlan reachabilityPlan = CPPReachabilityPlanner.Build(program, buildUsageReport, Options.FeatureCatalog);

            for (int i = 0; i < reachabilityPlan.Types.Count; i++) {
                ConversionClass cl = reachabilityPlan.Types[i];
                if (cl.IsNative) {
                    continue;
                }
                if (!ShouldEmitGeneratedSourceClass(cl)) {
                    continue;
                }

                string filePath = Path.Combine(folder, cl.GetEmittedFileStem(program));
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
        /// Returns whether one converted type should emit standalone generated C++ source files.
        /// </summary>
        /// <param name="conversionClass">Converted type to inspect.</param>
        /// <returns>True when the type should emit generated C++ source files; otherwise false.</returns>
        static bool ShouldEmitGeneratedSourceClass(ConversionClass conversionClass) {
            if (conversionClass == null) {
                return false;
            }

            return !string.Equals(conversionClass.Name, "NativeFreeFunctionAttribute", StringComparison.Ordinal) &&
                !string.Equals(conversionClass.Name, "NativeNoEscapeAttribute", StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves the feature usage report for the currently loaded Roslyn project.
        /// </summary>
        /// <returns>The resolved build usage report for the current conversion.</returns>
        CPPBuildUsageReport ResolveBuildUsageReport() {
            if (project == null) {
                return CPPFeatureResolver.Resolve(Options.BuildFeatureProfile, Options.FeatureCatalog, Array.Empty<CPPFeatureUsageRoot>());
            }

            Compilation compilation = AsyncUtil.RunSync(() => project.GetCompilationAsync());
            if (compilation is not CSharpCompilation csharpCompilation) {
                return CPPFeatureResolver.Resolve(Options.BuildFeatureProfile, Options.FeatureCatalog, Array.Empty<CPPFeatureUsageRoot>());
            }

            IReadOnlyList<CPPFeatureUsageRoot> detectedRoots = CPPFeatureScanner.Scan(csharpCompilation, Options.FeatureCatalog);
            return CPPFeatureResolver.Resolve(Options.BuildFeatureProfile, Options.FeatureCatalog, detectedRoots);
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
