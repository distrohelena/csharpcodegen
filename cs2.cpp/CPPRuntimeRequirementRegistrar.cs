namespace cs2.cpp {
    /// <summary>
    /// Tracks the runtime support requirements requested during a C++ conversion run.
    /// </summary>
    public class CPPRuntimeRequirementRegistrar {
        readonly CPPRuntimeRequirementCatalog catalog;
        CPPBuildUsageReport buildUsageReport;
        readonly Dictionary<string, CPPRuntimeRequirementDefinition> registeredRequirements;
        readonly CPPConversionReport report;

        /// <summary>
        /// Initializes a runtime requirement registrar.
        /// </summary>
        /// <param name="catalog">Catalog of known runtime requirements.</param>
        /// <param name="report">Conversion report used for unknown requirement diagnostics.</param>
        public CPPRuntimeRequirementRegistrar(CPPRuntimeRequirementCatalog catalog, CPPConversionReport report) {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.report = report ?? throw new ArgumentNullException(nameof(report));
            buildUsageReport = new CPPBuildUsageReport();
            registeredRequirements = new Dictionary<string, CPPRuntimeRequirementDefinition>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets the requirements registered for the active conversion run.
        /// </summary>
        public IReadOnlyCollection<CPPRuntimeRequirementDefinition> RegisteredRequirements => registeredRequirements.Values.ToList();

        /// <summary>
        /// Registers the default runtime requirements implied by the selected conversion options.
        /// </summary>
        /// <param name="options">The active conversion options.</param>
        public void RegisterDefaults(CPPConversionOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            Register("NativeString");

            if (options.RuntimeProfile.UseStdVector) {
                Register("NativeList");
            }

            if (options.RuntimeProfile.UseStdUnorderedMap) {
                Register("NativeDictionary");
            }
        }

        /// <summary>
        /// Applies the resolved feature decisions that should gate feature-owned runtime requirements.
        /// </summary>
        /// <param name="buildUsageReport">Resolved feature decisions for the active build.</param>
        public void ApplyBuildUsageReport(CPPBuildUsageReport buildUsageReport) {
            this.buildUsageReport = buildUsageReport ?? new CPPBuildUsageReport();
        }

        /// <summary>
        /// Clears the registered runtime requirement set so a new conversion run can rebuild it.
        /// </summary>
        public void Reset() {
            registeredRequirements.Clear();
        }

        /// <summary>
        /// Registers a runtime requirement by its stable catalog name.
        /// </summary>
        /// <param name="name">The stable runtime requirement name.</param>
        /// <returns>True when the requirement was found and registered.</returns>
        public bool Register(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Runtime requirement name must not be empty.", nameof(name));
            }

            if (!catalog.TryGet(name, out CPPRuntimeRequirementDefinition definition)) {
                report.AddDiagnostic(CPPDiagnosticSeverity.Error, "CPPREQ001", $"Unknown runtime requirement '{name}'.");
                return false;
            }

            if (!IsRequirementEnabled(definition)) {
                return false;
            }

            registeredRequirements.TryAdd(definition.Name, definition);
            return true;
        }

        /// <summary>
        /// Determines whether a named runtime requirement has already been registered.
        /// </summary>
        /// <param name="name">The stable runtime requirement name.</param>
        /// <returns>True when the requirement has been registered.</returns>
        public bool IsRegistered(string name) {
            return registeredRequirements.ContainsKey(name);
        }

        bool IsRequirementEnabled(CPPRuntimeRequirementDefinition definition) {
            if (definition == null) {
                throw new ArgumentNullException(nameof(definition));
            }

            if (definition.OwningFeatures.Count == 0) {
                return true;
            }

            foreach (CPPFeatureKind owningFeature in definition.OwningFeatures) {
                if (buildUsageReport.IsEnabled(owningFeature)) {
                    return true;
                }
            }

            return false;
        }
    }
}
