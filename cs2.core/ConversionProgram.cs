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

        public ConversionProgram(ConversionRules rules) {
            Classes = new List<ConversionClass>();
            TypeMap = new Dictionary<string, string>();
            Rules = rules;
            QualifiedGeneratedClassLookup = new Dictionary<string, ConversionClass>(StringComparer.Ordinal);
            QualifiedGeneratedClassLookupCount = -1;
            GeneratedClassLookupByNameAndArity = new Dictionary<string, ConversionClass>(StringComparer.Ordinal);
            GeneratedClassLookupByNameAndArityCount = -1;
            BaseEmittedTypeNameCollisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BaseEmittedTypeNameCollisionCount = -1;
        }

        public Dictionary<string, ConversionClass> GetQualifiedGeneratedClassLookup(Func<ConversionClass, string> keySelector) {
            if (keySelector == null) {
                throw new ArgumentNullException(nameof(keySelector));
            }

            if (QualifiedGeneratedClassLookupCount == Classes.Count) {
                return QualifiedGeneratedClassLookup;
            }

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
    }
}
