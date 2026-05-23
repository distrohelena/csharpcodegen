using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Computes the set of source types that remain reachable after feature pruning has been resolved.
    /// </summary>
    public static class CPPReachabilityPlanner {
        /// <summary>
        /// Builds a reachability plan for the supplied conversion program and resolved feature report.
        /// </summary>
        /// <param name="program">The conversion program to filter.</param>
        /// <param name="report">The resolved feature usage report.</param>
        /// <param name="featureCatalog">External feature catalog that defines feature-owned type roots.</param>
        /// <returns>The resulting reachability plan.</returns>
        public static CPPReachabilityPlan Build(ConversionProgram program, CPPBuildUsageReport report, CPPExternalFeatureCatalog featureCatalog) {
            CPPReachabilityPlan plan = new CPPReachabilityPlan();

            foreach (ConversionClass conversionClass in program.Classes) {
                if (!ShouldIncludeType(conversionClass, report, featureCatalog)) {
                    continue;
                }

                plan.Types.Add(conversionClass);
            }

            return plan;
        }

        /// <summary>
        /// Determines whether a converted type should remain in the reachable output set.
        /// </summary>
        /// <param name="conversionClass">The converted type to inspect.</param>
        /// <param name="report">The resolved feature usage report.</param>
        /// <param name="featureCatalog">External feature catalog that defines which types own which caller-owned features.</param>
        /// <returns><c>true</c> when the type should be kept; otherwise <c>false</c>.</returns>
        static bool ShouldIncludeType(ConversionClass conversionClass, CPPBuildUsageReport report, CPPExternalFeatureCatalog featureCatalog = null) {
            if (conversionClass.TypeSymbol == null) {
                return true;
            }

            Dictionary<string, IReadOnlyList<string>> ruleMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (CPPExternalFeatureRootRule rootRule in (featureCatalog ?? CPPExternalFeatureCatalog.Empty).RootRules) {
                ruleMap[rootRule.TypeName] = rootRule.FeatureIds;
            }

            if (!ruleMap.TryGetValue(conversionClass.TypeSymbol.ToDisplayString(), out IReadOnlyList<string> features)) {
                return true;
            }

            foreach (string feature in features) {
                if (!report.IsEnabled(feature)) {
                    return false;
                }
            }

            return true;
        }
    }
}
