namespace cs2.core {
    public class ConversionProgram {
        public List<ConversionClass> Classes { get; private set; }
        public Dictionary<string, string> TypeMap { get; private set; }
        public ConversionRules Rules { get; private set; }

        Dictionary<string, ConversionClass> QualifiedGeneratedClassLookup;
        int QualifiedGeneratedClassLookupCount;
        Dictionary<string, ConversionClass> GeneratedClassLookupByNameAndArity;
        int GeneratedClassLookupByNameAndArityCount;
        HashSet<string> BaseEmittedTypeNameCollisions;
        int BaseEmittedTypeNameCollisionCount;

        /// <summary>
        /// True while parallel emission reads the generated class lookups, so a rebuild triggered from a worker thread is a bug instead of a cache miss; written by the main thread only and read by every worker, hence volatile.
        /// </summary>
        volatile bool LookupsFrozen;

        public ConversionProgram(ConversionRules rules) {
            Classes = new List<ConversionClass>();
            TypeMap = new Dictionary<string, string>();
            Rules = rules;
            QualifiedGeneratedClassLookup = new Dictionary<string, ConversionClass>(StringComparer.Ordinal);
            QualifiedGeneratedClassLookupCount = -1;
            GeneratedClassLookupByNameAndArity = new Dictionary<string, ConversionClass>(StringComparer.Ordinal);
            GeneratedClassLookupByNameAndArityCount = -1;
            BaseEmittedTypeNameCollisions = new HashSet<string>(StringComparer.Ordinal);
            BaseEmittedTypeNameCollisionCount = -1;
        }

        /// <summary>
        /// Marks the generated class lookups read-only for the duration of a parallel emission pass, so any rebuild attempt fails loudly instead of racing the workers that read them.
        /// </summary>
        /// <remarks>
        /// The lookups are lazily rebuilt whenever <see cref="Classes"/> grows. During parallel emission every worker reads them without a lock, which is only safe while the dictionaries are immutable, so the freeze converts a silent data race into an <see cref="InvalidOperationException"/>. Callers must warm every lookup before freezing.
        /// </remarks>
        public void FreezeGeneratedClassLookups() {
            LookupsFrozen = true;
        }

        /// <summary>
        /// Allows the generated class lookups to be rebuilt again once a parallel emission pass has finished.
        /// </summary>
        /// <remarks>
        /// Must run on every exit path of the pass, including aborts, or later single-threaded phases would fail to rebuild a stale lookup.
        /// </remarks>
        public void ThawGeneratedClassLookups() {
            LookupsFrozen = false;
        }

        public Dictionary<string, ConversionClass> GetQualifiedGeneratedClassLookup(Func<ConversionClass, string> keySelector) {
            if (keySelector == null) {
                throw new ArgumentNullException(nameof(keySelector));
            }

            if (QualifiedGeneratedClassLookupCount == Classes.Count) {
                return QualifiedGeneratedClassLookup;
            }

            ThrowIfLookupsFrozen();

            QualifiedGeneratedClassLookup = new Dictionary<string, ConversionClass>(StringComparer.Ordinal);
            foreach (ConversionClass conversionClass in Classes) {
                if (conversionClass == null || conversionClass.IsNative) {
                    continue;
                }

                string key = keySelector(conversionClass);
                if (string.IsNullOrWhiteSpace(key) || QualifiedGeneratedClassLookup.ContainsKey(key)) {
                    continue;
                }

                QualifiedGeneratedClassLookup.Add(key, conversionClass);
            }

            QualifiedGeneratedClassLookupCount = Classes.Count;
            return QualifiedGeneratedClassLookup;
        }

        public Dictionary<string, ConversionClass> GetGeneratedClassLookupByNameAndArity(Func<ConversionClass, string> keySelector) {
            if (keySelector == null) {
                throw new ArgumentNullException(nameof(keySelector));
            }

            if (GeneratedClassLookupByNameAndArityCount == Classes.Count) {
                return GeneratedClassLookupByNameAndArity;
            }

            ThrowIfLookupsFrozen();

            GeneratedClassLookupByNameAndArity = new Dictionary<string, ConversionClass>(StringComparer.Ordinal);
            foreach (ConversionClass conversionClass in Classes) {
                if (conversionClass == null || conversionClass.IsNative) {
                    continue;
                }

                string key = keySelector(conversionClass);
                if (string.IsNullOrWhiteSpace(key) || GeneratedClassLookupByNameAndArity.ContainsKey(key)) {
                    continue;
                }

                GeneratedClassLookupByNameAndArity.Add(key, conversionClass);
            }

            GeneratedClassLookupByNameAndArityCount = Classes.Count;
            return GeneratedClassLookupByNameAndArity;
        }

        public HashSet<string> GetBaseEmittedTypeNameCollisions(Func<ConversionClass, string> keySelector) {
            if (keySelector == null) {
                throw new ArgumentNullException(nameof(keySelector));
            }

            if (BaseEmittedTypeNameCollisionCount == Classes.Count) {
                return BaseEmittedTypeNameCollisions;
            }

            ThrowIfLookupsFrozen();

            BaseEmittedTypeNameCollisions = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConversionClass conversionClass in Classes) {
                if (conversionClass == null || conversionClass.IsNative) {
                    continue;
                }

                string key = keySelector(conversionClass);
                if (string.IsNullOrWhiteSpace(key)) {
                    continue;
                }

                if (!seen.Add(key)) {
                    BaseEmittedTypeNameCollisions.Add(key);
                }
            }

            BaseEmittedTypeNameCollisionCount = Classes.Count;
            return BaseEmittedTypeNameCollisions;
        }

        /// <summary>
        /// Rejects a lookup rebuild while the lookups are frozen for parallel emission.
        /// </summary>
        /// <exception cref="InvalidOperationException">The class list changed after the lookups were warmed and frozen.</exception>
        void ThrowIfLookupsFrozen() {
            if (LookupsFrozen) {
                throw new InvalidOperationException("Generated class lookups cannot be rebuilt while parallel emission is running; the class list changed after the lookups were warmed.");
            }
        }
    }
}
