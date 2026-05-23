namespace cs2.cpp {
    /// <summary>
    /// Defines the build-time feature selections and conflict policies for a conversion run.
    /// </summary>
    public class CPPBuildFeatureProfile {
        /// <summary>
        /// Stores the requested selection mode for each feature.
        /// </summary>
        readonly Dictionary<string, CPPFeatureMode> Modes = new Dictionary<string, CPPFeatureMode>(StringComparer.Ordinal);

        /// <summary>
        /// Stores the conflict policy for each feature when detected usage contradicts the profile.
        /// </summary>
        readonly Dictionary<string, CPPFeatureConflictPolicy> ConflictPolicies = new Dictionary<string, CPPFeatureConflictPolicy>(StringComparer.Ordinal);

        /// <summary>
        /// Gets the selected mode for a feature.
        /// </summary>
        /// <param name="featureId">Caller-owned feature id being queried.</param>
        /// <param name="defaultMode">Default mode to use when the profile has no override.</param>
        /// <returns>The requested feature mode.</returns>
        public CPPFeatureMode GetMode(string featureId, CPPFeatureMode defaultMode) {
            if (string.IsNullOrWhiteSpace(featureId)) {
                return defaultMode;
            }

            if (Modes.TryGetValue(featureId, out CPPFeatureMode mode)) {
                return mode;
            }

            return defaultMode;
        }

        /// <summary>
        /// Gets the conflict policy for a feature.
        /// </summary>
        /// <param name="featureId">Caller-owned feature id being queried.</param>
        /// <param name="defaultPolicy">Default conflict policy to use when the profile has no override.</param>
        /// <returns>The requested conflict policy.</returns>
        public CPPFeatureConflictPolicy GetConflictPolicy(string featureId, CPPFeatureConflictPolicy defaultPolicy) {
            if (string.IsNullOrWhiteSpace(featureId)) {
                return defaultPolicy;
            }

            if (ConflictPolicies.TryGetValue(featureId, out CPPFeatureConflictPolicy policy)) {
                return policy;
            }

            return defaultPolicy;
        }

        /// <summary>
        /// Assigns a feature mode and returns the same profile for fluent configuration.
        /// </summary>
        /// <param name="featureId">Caller-owned feature id being configured.</param>
        /// <param name="mode">Requested selection mode.</param>
        /// <returns>The updated build feature profile.</returns>
        public CPPBuildFeatureProfile WithMode(string featureId, CPPFeatureMode mode) {
            if (string.IsNullOrWhiteSpace(featureId)) {
                throw new ArgumentException("Feature id must not be empty.", nameof(featureId));
            }

            Modes[featureId] = mode;
            return this;
        }

        /// <summary>
        /// Assigns a feature conflict policy and returns the same profile for fluent configuration.
        /// </summary>
        /// <param name="featureId">Caller-owned feature id being configured.</param>
        /// <param name="policy">Conflict policy for forced-disable contradictions.</param>
        /// <returns>The updated build feature profile.</returns>
        public CPPBuildFeatureProfile WithConflictPolicy(string featureId, CPPFeatureConflictPolicy policy) {
            if (string.IsNullOrWhiteSpace(featureId)) {
                throw new ArgumentException("Feature id must not be empty.", nameof(featureId));
            }

            ConflictPolicies[featureId] = policy;
            return this;
        }

        /// <summary>
        /// Creates the default profile for the phase-one feature buckets.
        /// </summary>
        /// <returns>An empty build feature profile that defers feature defaults to the external feature catalog.</returns>
        public static CPPBuildFeatureProfile CreateDefault() {
            return new CPPBuildFeatureProfile();
        }
    }
}
