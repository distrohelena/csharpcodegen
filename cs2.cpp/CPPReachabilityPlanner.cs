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
            Dictionary<string, IReadOnlyList<string>> ruleMap = BuildRuleMap(featureCatalog);

            foreach (ConversionClass conversionClass in program.Classes) {
                if (!ShouldIncludeType(conversionClass, report, ruleMap)) {
                    continue;
                }

                plan.Types.Add(conversionClass);
            }

            ExpandRequiredGeneratedTypeDependencies(program, plan.Types);
            return plan;
        }

        /// <summary>
        /// Determines whether a converted type should remain in the reachable output set.
        /// </summary>
        /// <param name="conversionClass">The converted type to inspect.</param>
        /// <param name="report">The resolved feature usage report.</param>
        /// <param name="featureCatalog">External feature catalog that defines which types own which caller-owned features.</param>
        /// <returns><c>true</c> when the type should be kept; otherwise <c>false</c>.</returns>
        static bool ShouldIncludeType(ConversionClass conversionClass, CPPBuildUsageReport report, IReadOnlyDictionary<string, IReadOnlyList<string>> ruleMap) {
            if (conversionClass.TypeSymbol == null) {
                return true;
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

        /// <summary>
        /// Builds one lookup from fully qualified type names to the caller-owned features that own them.
        /// </summary>
        /// <param name="featureCatalog">External feature catalog that defines feature-owned type roots.</param>
        /// <returns>Case-sensitive root-rule lookup keyed by fully qualified type name.</returns>
        static Dictionary<string, IReadOnlyList<string>> BuildRuleMap(CPPExternalFeatureCatalog featureCatalog) {
            Dictionary<string, IReadOnlyList<string>> ruleMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (CPPExternalFeatureRootRule rootRule in (featureCatalog ?? CPPExternalFeatureCatalog.Empty).RootRules) {
                ruleMap[rootRule.TypeName] = rootRule.FeatureIds;
            }

            return ruleMap;
        }

        /// <summary>
        /// Ensures every surviving generated type keeps the generated types needed by its signatures and explicit references so emitted source never includes missing headers.
        /// </summary>
        /// <param name="program">The conversion program that owns all generated classes.</param>
        /// <param name="includedTypes">The currently included type set that will be expanded in place.</param>
        static void ExpandRequiredGeneratedTypeDependencies(ConversionProgram program, IList<ConversionClass> includedTypes) {
            if (program == null || includedTypes == null || includedTypes.Count == 0) {
                return;
            }

            Queue<ConversionClass> pendingTypes = new Queue<ConversionClass>(includedTypes);
            HashSet<string> includedTypeNames = new HashSet<string>(
                includedTypes.Select(static conversionClass => conversionClass.GetEmittedTypeName()),
                StringComparer.Ordinal);

            while (pendingTypes.Count > 0) {
                ConversionClass currentType = pendingTypes.Dequeue();
                foreach (ConversionClass dependency in EnumerateGeneratedDependencies(program, currentType)) {
                    if (dependency == null ||
                        dependency.IsNative ||
                        !includedTypeNames.Add(dependency.GetEmittedTypeName())) {
                        continue;
                    }

                    includedTypes.Add(dependency);
                    pendingTypes.Enqueue(dependency);
                }
            }
        }

        /// <summary>
        /// Enumerates generated classes that the supplied type depends on through inheritance, member signatures, or explicit referenced-class tracking.
        /// </summary>
        /// <param name="program">Conversion program used to resolve generated classes.</param>
        /// <param name="conversionClass">The surviving type whose generated dependencies should be retained.</param>
        /// <returns>Generated classes required to keep the surviving type compilable.</returns>
        static IEnumerable<ConversionClass> EnumerateGeneratedDependencies(ConversionProgram program, ConversionClass conversionClass) {
            if (program == null || conversionClass == null) {
                yield break;
            }

            HashSet<string> emittedDependencyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string extension in conversionClass.Extensions.Distinct(StringComparer.Ordinal)) {
                if (TryResolveGeneratedClass(program, VariableUtil.GetVarType(extension), out ConversionClass dependency) &&
                    emittedDependencyNames.Add(dependency.GetEmittedTypeName())) {
                    yield return dependency;
                }
            }

            foreach (string referencedClass in conversionClass.ReferencedClasses.Distinct(StringComparer.Ordinal)) {
                if (TryResolveGeneratedClass(program, VariableUtil.GetVarType(referencedClass), out ConversionClass dependency) &&
                    emittedDependencyNames.Add(dependency.GetEmittedTypeName())) {
                    yield return dependency;
                }
            }

            foreach (ConversionVariable variable in conversionClass.Variables) {
                foreach (ConversionClass dependency in EnumerateGeneratedDependencies(program, variable.VarType, emittedDependencyNames)) {
                    yield return dependency;
                }
            }

            foreach (ConversionFunction function in conversionClass.Functions) {
                foreach (ConversionClass dependency in EnumerateGeneratedDependencies(program, function.ReturnType, emittedDependencyNames)) {
                    yield return dependency;
                }

                foreach (ConversionVariable parameter in function.InParameters) {
                    foreach (ConversionClass dependency in EnumerateGeneratedDependencies(program, parameter.VarType, emittedDependencyNames)) {
                        yield return dependency;
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates generated dependencies referenced by one abstract variable type, including nested generic arguments.
        /// </summary>
        /// <param name="program">Conversion program used to resolve generated classes.</param>
        /// <param name="variableType">The type metadata to inspect.</param>
        /// <param name="emittedDependencyNames">Set used to suppress duplicate generated dependencies.</param>
        /// <returns>Generated classes referenced by the supplied type metadata.</returns>
        static IEnumerable<ConversionClass> EnumerateGeneratedDependencies(
            ConversionProgram program,
            VariableType variableType,
            ISet<string> emittedDependencyNames) {
            if (program == null || variableType == null || emittedDependencyNames == null) {
                yield break;
            }

            if (TryResolveGeneratedClass(program, variableType, out ConversionClass dependency) &&
                emittedDependencyNames.Add(dependency.GetEmittedTypeName())) {
                yield return dependency;
            }

            foreach (VariableType genericArgument in variableType.GenericArgs) {
                foreach (ConversionClass nestedDependency in EnumerateGeneratedDependencies(program, genericArgument, emittedDependencyNames)) {
                    yield return nestedDependency;
                }
            }
        }

        /// <summary>
        /// Resolves one abstract variable type back to a generated class in the active conversion program.
        /// </summary>
        /// <param name="program">Conversion program used to resolve generated classes.</param>
        /// <param name="variableType">The abstract type metadata being resolved.</param>
        /// <param name="conversionClass">Resolved generated class when present.</param>
        /// <returns><c>true</c> when the type resolves to a generated class; otherwise <c>false</c>.</returns>
        static bool TryResolveGeneratedClass(ConversionProgram program, VariableType variableType, out ConversionClass conversionClass) {
            conversionClass = null;
            if (program == null || variableType == null || string.IsNullOrWhiteSpace(variableType.TypeName)) {
                return false;
            }

            conversionClass = program.FindGeneratedClass(variableType.TypeName, variableType.GenericArgs.Count);
            return conversionClass != null;
        }
    }
}
