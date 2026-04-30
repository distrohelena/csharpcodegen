namespace cs2.cpp {
    /// <summary>
    /// Resolves the final enabled state of features from explicit build settings and detected usage roots.
    /// </summary>
    public static class CPPFeatureResolver {
        /// <summary>
        /// Builds a usage report from the supplied profile and detected feature roots.
        /// </summary>
        /// <param name="profile">The configured build feature profile.</param>
        /// <param name="detectedRoots">The detected feature usage roots.</param>
        /// <returns>The resolved usage report.</returns>
        public static CPPBuildUsageReport Resolve(CPPBuildFeatureProfile profile, IEnumerable<CPPFeatureUsageRoot> detectedRoots) {
            CPPBuildUsageReport report = new CPPBuildUsageReport();

            foreach (CPPFeatureUsageRoot detectedRoot in detectedRoots) {
                report.DetectedRoots.Add(detectedRoot);
            }

            foreach (CPPFeatureKind feature in Enum.GetValues<CPPFeatureKind>()) {
                bool isDetected = HasDetectedRoot(report.DetectedRoots, feature);
                CPPFeatureMode mode = profile.GetMode(feature);

                if (mode == CPPFeatureMode.Disabled) {
                    report.FeatureDecisions.Add(new CPPFeatureDecision {
                        Feature = feature,
                        Enabled = false,
                        Origin = CPPFeatureDecisionOrigin.ForcedDisabled,
                    });

                    if (isDetected) {
                        report.Conflicts.Add(new CPPFeatureConflict {
                            Feature = feature,
                            Policy = profile.GetConflictPolicy(feature),
                            Message = $"Feature '{feature}' was detected but the build profile force-disabled it.",
                        });
                    }

                    continue;
                } else if (mode == CPPFeatureMode.Enabled) {
                    report.FeatureDecisions.Add(new CPPFeatureDecision {
                        Feature = feature,
                        Enabled = true,
                        Origin = CPPFeatureDecisionOrigin.ForcedEnabled,
                    });
                    continue;
                } else if (isDetected) {
                    report.FeatureDecisions.Add(new CPPFeatureDecision {
                        Feature = feature,
                        Enabled = true,
                        Origin = CPPFeatureDecisionOrigin.AutoDetected,
                    });
                    continue;
                }

                report.FeatureDecisions.Add(new CPPFeatureDecision {
                    Feature = feature,
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
        /// <param name="feature">The feature to locate.</param>
        /// <returns><c>true</c> when the feature was detected; otherwise <c>false</c>.</returns>
        static bool HasDetectedRoot(IEnumerable<CPPFeatureUsageRoot> detectedRoots, CPPFeatureKind feature) {
            foreach (CPPFeatureUsageRoot detectedRoot in detectedRoots) {
                if (detectedRoot.Feature == feature) {
                    return true;
                }
            }

            return false;
        }
    }
}
