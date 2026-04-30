namespace cs2.cpp {
    /// <summary>
    /// Defines the build-time feature selections and conflict policies for a conversion run.
    /// </summary>
    public class CPPBuildFeatureProfile {
        /// <summary>
        /// Stores the requested selection mode for each feature.
        /// </summary>
        readonly Dictionary<CPPFeatureKind, CPPFeatureMode> Modes = new Dictionary<CPPFeatureKind, CPPFeatureMode>();

        /// <summary>
        /// Stores the conflict policy for each feature when detected usage contradicts the profile.
        /// </summary>
        readonly Dictionary<CPPFeatureKind, CPPFeatureConflictPolicy> ConflictPolicies = new Dictionary<CPPFeatureKind, CPPFeatureConflictPolicy>();

        /// <summary>
        /// Gets the selected mode for a feature.
        /// </summary>
        /// <param name="feature">Feature being queried.</param>
        /// <returns>The requested feature mode.</returns>
        public CPPFeatureMode GetMode(CPPFeatureKind feature) {
            if (Modes.TryGetValue(feature, out CPPFeatureMode mode)) {
                return mode;
            }

            return CPPFeatureMode.Auto;
        }

        /// <summary>
        /// Gets the conflict policy for a feature.
        /// </summary>
        /// <param name="feature">Feature being queried.</param>
        /// <returns>The requested conflict policy.</returns>
        public CPPFeatureConflictPolicy GetConflictPolicy(CPPFeatureKind feature) {
            if (ConflictPolicies.TryGetValue(feature, out CPPFeatureConflictPolicy policy)) {
                return policy;
            }

            return CPPFeatureConflictPolicy.Error;
        }

        /// <summary>
        /// Assigns a feature mode and returns the same profile for fluent configuration.
        /// </summary>
        /// <param name="feature">Feature being configured.</param>
        /// <param name="mode">Requested selection mode.</param>
        /// <returns>The updated build feature profile.</returns>
        public CPPBuildFeatureProfile WithMode(CPPFeatureKind feature, CPPFeatureMode mode) {
            Modes[feature] = mode;
            return this;
        }

        /// <summary>
        /// Assigns a feature conflict policy and returns the same profile for fluent configuration.
        /// </summary>
        /// <param name="feature">Feature being configured.</param>
        /// <param name="policy">Conflict policy for forced-disable contradictions.</param>
        /// <returns>The updated build feature profile.</returns>
        public CPPBuildFeatureProfile WithConflictPolicy(CPPFeatureKind feature, CPPFeatureConflictPolicy policy) {
            ConflictPolicies[feature] = policy;
            return this;
        }

        /// <summary>
        /// Creates the default profile for the phase-one feature buckets.
        /// </summary>
        /// <returns>A build feature profile with automatic selection defaults.</returns>
        public static CPPBuildFeatureProfile CreateDefault() {
            CPPBuildFeatureProfile profile = new CPPBuildFeatureProfile();
            profile.WithMode(CPPFeatureKind.Render2D, CPPFeatureMode.Auto);
            profile.WithMode(CPPFeatureKind.Sprites, CPPFeatureMode.Auto);
            profile.WithMode(CPPFeatureKind.Text2D, CPPFeatureMode.Auto);
            profile.WithMode(CPPFeatureKind.Shaders, CPPFeatureMode.Auto);
            profile.WithMode(CPPFeatureKind.DebugOverlay, CPPFeatureMode.Auto);
            return profile;
        }
    }
}
