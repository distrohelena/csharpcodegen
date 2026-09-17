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
        /// Every distinct non-empty emitted type name in the snapshot, in first-occurrence program order.
        /// </summary>
        readonly List<string> OrderedEmittedTypeNameList;

        /// <summary>
        /// Whether every emitted type name in the snapshot consists solely of identifier characters (letters, digits, underscore).
        /// </summary>
        readonly bool AllNamesAreIdentifiers;

        /// <summary>
        /// Initializes an index from prebuilt lookups.
        /// </summary>
        /// <param name="emittedTypeNamesByClass">Emitted names keyed by class identity.</param>
        /// <param name="generatedClassesByEmittedName">First generated class per emitted name.</param>
        /// <param name="allEmittedTypeNames">Every emitted name in the snapshot.</param>
        /// <param name="orderedEmittedTypeNameList">Every distinct emitted name in first-occurrence program order.</param>
        /// <param name="allNamesAreIdentifiers">Whether every emitted name is composed solely of identifier characters.</param>
        CPPEmittedTypeNameIndex(
            Dictionary<ConversionClass, string> emittedTypeNamesByClass,
            Dictionary<string, ConversionClass> generatedClassesByEmittedName,
            HashSet<string> allEmittedTypeNames,
            List<string> orderedEmittedTypeNameList,
            bool allNamesAreIdentifiers) {
            EmittedTypeNamesByClass = emittedTypeNamesByClass;
            GeneratedClassesByEmittedName = generatedClassesByEmittedName;
            AllEmittedTypeNames = allEmittedTypeNames;
            OrderedEmittedTypeNameList = orderedEmittedTypeNameList;
            AllNamesAreIdentifiers = allNamesAreIdentifiers;
        }

        /// <summary>
        /// Gets every non-empty emitted type name in the snapshot.
        /// </summary>
        public IReadOnlySet<string> EmittedTypeNames => AllEmittedTypeNames;

        /// <summary>
        /// Gets every distinct non-empty emitted type name in the snapshot, in first-occurrence program order.
        /// Used to replay the exact regex-based qualification order when an emitted name is not a pure identifier.
        /// </summary>
        public IReadOnlyList<string> OrderedEmittedTypeNames => OrderedEmittedTypeNameList;

        /// <summary>
        /// Gets whether every emitted type name in the snapshot consists solely of identifier characters
        /// (letters, digits, underscore). When false, at least one emitted name can only be matched with the
        /// original word-boundary regex, since it contains characters a maximal identifier scan cannot span.
        /// </summary>
        public bool AllEmittedTypeNamesAreIdentifiers => AllNamesAreIdentifiers;

        /// <summary>
        /// Builds the index by computing each class's emitted name exactly once in program order. The collision
        /// assertion only runs for emittable classes (non-native classes the generated-type emission policy
        /// would actually emit) — the same set the real emission path resolves emitted names for. Every other
        /// class records its unasserted base emitted name so lookups still resolve without ever raising a
        /// collision the emitter itself would never have checked.
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
            List<string> orderedEmittedTypeNameList = new List<string>();
            bool allNamesAreIdentifiers = true;

            for (int index = 0; index < classes.Count; index++) {
                ConversionClass conversionClass = classes[index];
                if (conversionClass == null) {
                    continue;
                }

                bool emittable = !conversionClass.IsNative && CPPGeneratedTypeEmissionPolicy.ShouldEmit(conversionClass);
                string emittedTypeName = emittable
                    ? CPPVariableType.ComputeEmittedTypeName(conversionClass)
                    : CPPVariableType.ComputeBaseEmittedTypeName(conversionClass);
                emittedTypeNamesByClass[conversionClass] = emittedTypeName;
                if (string.IsNullOrWhiteSpace(emittedTypeName)) {
                    continue;
                }

                if (allEmittedTypeNames.Add(emittedTypeName)) {
                    orderedEmittedTypeNameList.Add(emittedTypeName);
                    if (allNamesAreIdentifiers && !IsIdentifierName(emittedTypeName)) {
                        allNamesAreIdentifiers = false;
                    }
                }

                if (emittable) {
                    generatedClassesByEmittedName.TryAdd(emittedTypeName, conversionClass);
                }
            }

            return new CPPEmittedTypeNameIndex(
                emittedTypeNamesByClass,
                generatedClassesByEmittedName,
                allEmittedTypeNames,
                orderedEmittedTypeNameList,
                allNamesAreIdentifiers);
        }

        /// <summary>
        /// Determines whether an emitted type name consists solely of identifier characters.
        /// </summary>
        /// <param name="emittedTypeName">Emitted type name to classify.</param>
        /// <returns>True when every character in the name is an identifier character.</returns>
        static bool IsIdentifierName(string emittedTypeName) {
            for (int characterIndex = 0; characterIndex < emittedTypeName.Length; characterIndex++) {
                if (!CPPGeneratedTypeNameQualifier.IsIdentifierCharacter(emittedTypeName[characterIndex])) {
                    return false;
                }
            }

            return true;
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
