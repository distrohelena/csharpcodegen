using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Represents the set of source types that remain reachable for a feature-pruned build.
    /// </summary>
    public class CPPReachabilityPlan {
        /// <summary>
        /// Gets the source types that should remain in the generated output.
        /// </summary>
        public List<ConversionClass> Types { get; } = new List<ConversionClass>();
    }
}
