namespace cs2.cpp {
    /// <summary>
    /// Resolves the final enabled state of features from explicit build settings and detected usage roots.
    /// </summary>
    public static class CPPFeatureResolver {
        /// <summary>
        /// Builds a usage report from the supplied profile and detected feature roots.
        /// </summary>
        /// <param name="profile">The configured build feature profile.</param>
        /// <param name="featureCatalog">External feature catalog that defines valid caller-owned features.</param>
        /// <param name="detectedRoots">The detected feature usage roots.</param>
        /// <returns>The resolved usage report.</returns>
        public static CPPBuildUsageReport Resolve(
            CPPBuildFeatureProfile profile,
            CPPExternalFeatureCatalog featureCatalog,
            IEnumerable<CPPFeatureUsageRoot> detectedRoots) {
            CPPBuildUsageReport report = new CPPBuildUsageReport();
            CPPBuildFeatureProfile resolvedProfile = profile ?? CPPBuildFeatureProfile.CreateDefault();
            CPPExternalFeatureCatalog resolvedCatalog = featureCatalog ?? CPPExternalFeatureCatalog.Empty;

            foreach (CPPFeatureUsageRoot detectedRoot in detectedRoots ?? Array.Empty<CPPFeatureUsageRoot>()) {
                report.DetectedRoots.Add(detectedRoot);
            }

            foreach (CPPExternalFeatureDefinition feature in resolvedCatalog.Features) {
                bool isDetected = HasDetectedRoot(report.DetectedRoots, feature.Id);
                CPPFeatureMode mode = resolvedProfile.GetMode(feature.Id, feature.DefaultMode);

                if (mode == CPPFeatureMode.Disabled) {
                    report.FeatureDecisions.Add(new CPPFeatureDecision {
                        FeatureId = feature.Id,
                        Enabled = false,
                        Origin = CPPFeatureDecisionOrigin.ForcedDisabled,
                    });

                    if (isDetected) {
                        report.Conflicts.Add(new CPPFeatureConflict {
                            FeatureId = feature.Id,
                            Policy = resolvedProfile.GetConflictPolicy(feature.Id, feature.ConflictPolicy),
                            Message = $"Feature '{feature.Id}' was detected but the build profile force-disabled it.",
                        });
                    }

                    continue;
                } else if (mode == CPPFeatureMode.Enabled) {
                    report.FeatureDecisions.Add(new CPPFeatureDecision {
                        FeatureId = feature.Id,
                        Enabled = true,
                        Origin = CPPFeatureDecisionOrigin.ForcedEnabled,
                    });
                    continue;
                } else if (isDetected) {
                    report.FeatureDecisions.Add(new CPPFeatureDecision {
                        FeatureId = feature.Id,
                        Enabled = true,
                        Origin = CPPFeatureDecisionOrigin.AutoDetected,
                    });
                    continue;
                }

                report.FeatureDecisions.Add(new CPPFeatureDecision {
                    FeatureId = feature.Id,
                    Enabled = false,
                    Origin = CPPFeatureDecisionOrigin.NotIncluded,
                });
            }

            return report;
        }

        /// <summary>
        /// Determines whether at least one detected root belongs to the specified feature.
        /// </summary>
        /// <param name="detectedRoots">The detected roots to inspect.</param>
        /// <param name="featureId">The caller-owned feature id to locate.</param>
        /// <returns><c>true</c> when the feature was detected; otherwise <c>false</c>.</returns>
        static bool HasDetectedRoot(IEnumerable<CPPFeatureUsageRoot> detectedRoots, string featureId) {
            foreach (CPPFeatureUsageRoot detectedRoot in detectedRoots) {
                if (string.Equals(detectedRoot.FeatureId, featureId, StringComparison.Ordinal)) {
                    return true;
                }
            }

            return false;
        }
    }
}
