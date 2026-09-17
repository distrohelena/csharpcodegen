using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Immutable snapshot of every class's emitted C++ type name, built once per emit pass so per-name lookups never rescan the program.
    /// </summary>
    public sealed class CPPEmittedTypeNameIndex {
        /// <summary>
        /// Emitted type name for every class in the snapshot, keyed by class identity.
        /// </summary>
        readonly Dictionary<ConversionClass, string> EmittedTypeNamesByClass;

        /// <summary>
        /// First generated, emittable class for each emitted type name in program order.
        /// </summary>
        readonly Dictionary<string, ConversionClass> GeneratedClassesByEmittedName;

        /// <summary>
        /// Every non-empty emitted type name in the snapshot, including native runtime classes.
        /// </summary>
        readonly HashSet<string> AllEmittedTypeNames;

        /// <summary>
        /// Initializes an index from prebuilt lookups.
        /// </summary>
        /// <param name="emittedTypeNamesByClass">Emitted names keyed by class identity.</param>
        /// <param name="generatedClassesByEmittedName">First generated class per emitted name.</param>
        /// <param name="allEmittedTypeNames">Every emitted name in the snapshot.</param>
        CPPEmittedTypeNameIndex(
            Dictionary<ConversionClass, string> emittedTypeNamesByClass,
            Dictionary<string, ConversionClass> generatedClassesByEmittedName,
            HashSet<string> allEmittedTypeNames) {
            EmittedTypeNamesByClass = emittedTypeNamesByClass;
            GeneratedClassesByEmittedName = generatedClassesByEmittedName;
            AllEmittedTypeNames = allEmittedTypeNames;
        }

        /// <summary>
        /// Gets every non-empty emitted type name in the snapshot.
        /// </summary>
        public IReadOnlySet<string> EmittedTypeNames => AllEmittedTypeNames;

        /// <summary>
        /// Builds the index by computing each class's emitted name exactly once in program order.
        /// </summary>
        /// <param name="classes">Program classes to snapshot.</param>
        /// <returns>The completed index.</returns>
        public static CPPEmittedTypeNameIndex Build(IReadOnlyList<ConversionClass> classes) {
            if (classes == null) {
                throw new ArgumentNullException(nameof(classes));
            }

            Dictionary<ConversionClass, string> emittedTypeNamesByClass = new Dictionary<ConversionClass, string>(ReferenceEqualityComparer.Instance);
            Dictionary<string, ConversionClass> generatedClassesByEmittedName = new Dictionary<string, ConversionClass>(StringComparer.Ordinal);
            HashSet<string> allEmittedTypeNames = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < classes.Count; index++) {
                ConversionClass conversionClass = classes[index];
                if (conversionClass == null) {
                    continue;
                }

                string emittedTypeName = CPPVariableType.ComputeEmittedTypeName(conversionClass);
                emittedTypeNamesByClass[conversionClass] = emittedTypeName;
                if (string.IsNullOrWhiteSpace(emittedTypeName)) {
                    continue;
                }

                allEmittedTypeNames.Add(emittedTypeName);
                if (!conversionClass.IsNative && CPPGeneratedTypeEmissionPolicy.ShouldEmit(conversionClass)) {
                    generatedClassesByEmittedName.TryAdd(emittedTypeName, conversionClass);
                }
            }

            return new CPPEmittedTypeNameIndex(emittedTypeNamesByClass, generatedClassesByEmittedName, allEmittedTypeNames);
        }

        /// <summary>
        /// Looks up the snapshot emitted name for one class.
        /// </summary>
        /// <param name="conversionClass">Class to resolve.</param>
        /// <param name="emittedTypeName">Emitted name recorded when the index was built.</param>
        /// <returns>True when the class was part of the snapshot.</returns>
        public bool TryGetEmittedTypeName(ConversionClass conversionClass, out string emittedTypeName) {
            emittedTypeName = string.Empty;
            return conversionClass != null && EmittedTypeNamesByClass.TryGetValue(conversionClass, out emittedTypeName);
        }

        /// <summary>
        /// Looks up the first generated, emittable class whose emitted name matches.
        /// </summary>
        /// <param name="emittedTypeName">Emitted type name to resolve.</param>
        /// <param name="generatedClass">Matching generated class when found.</param>
        /// <returns>True when a generated class carries the emitted name.</returns>
        public bool TryGetGeneratedClass(string emittedTypeName, out ConversionClass generatedClass) {
            generatedClass = null;
            return !string.IsNullOrWhiteSpace(emittedTypeName) && GeneratedClassesByEmittedName.TryGetValue(emittedTypeName, out generatedClass);
        }
    }
}
