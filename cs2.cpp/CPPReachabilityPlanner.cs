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
        /// <returns>The resulting reachability plan.</returns>
        public static CPPReachabilityPlan Build(ConversionProgram program, CPPBuildUsageReport report) {
            CPPReachabilityPlan plan = new CPPReachabilityPlan();

            foreach (ConversionClass conversionClass in program.Classes) {
                if (!ShouldIncludeType(conversionClass, report)) {
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
        /// <returns><c>true</c> when the type should be kept; otherwise <c>false</c>.</returns>
        static bool ShouldIncludeType(ConversionClass conversionClass, CPPBuildUsageReport report) {
            if (conversionClass.TypeSymbol == null) {
                return true;
            }

            if (!CPPFeatureCatalog.TryGetFeatures(conversionClass.TypeSymbol, out CPPFeatureKind[] features)) {
                return true;
            }

            foreach (CPPFeatureKind feature in features) {
                if (!report.IsEnabled(feature)) {
                    return false;
                }
            }

            return true;
        }
    }
}
