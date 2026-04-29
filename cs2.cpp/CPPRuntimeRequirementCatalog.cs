namespace cs2.cpp {
    /// <summary>
    /// Provides the known runtime requirements that the C++ backend can register.
    /// </summary>
    public class CPPRuntimeRequirementCatalog {
        readonly Dictionary<string, CPPRuntimeRequirementDefinition> definitions;

        /// <summary>
        /// Initializes the catalog with the built-in runtime requirements.
        /// </summary>
        public CPPRuntimeRequirementCatalog() {
            definitions = CreateDefinitions().ToDictionary(definition => definition.Name, StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets every known runtime requirement definition in the catalog.
        /// </summary>
        public IReadOnlyCollection<CPPRuntimeRequirementDefinition> Definitions => definitions.Values.ToList();

        /// <summary>
        /// Tries to resolve a runtime requirement definition by name.
        /// </summary>
        /// <param name="name">The stable runtime requirement name.</param>
        /// <param name="definition">Resolved definition when the lookup succeeds.</param>
        /// <returns>True when the requirement exists in the catalog.</returns>
        public bool TryGet(string name, out CPPRuntimeRequirementDefinition definition) {
            return definitions.TryGetValue(name, out definition);
        }

        static IEnumerable<CPPRuntimeRequirementDefinition> CreateDefinitions() {
            return new[] {
                Make("NativeArray", "runtime/array.hpp", "HE_CPP_REQ_NATIVE_ARRAY", "Managed-style array abstraction support."),
                Make("NativeString", "runtime/native_string.hpp", "HE_CPP_REQ_NATIVE_STRING", "Managed-style string abstraction support."),
                Make("NativeList", "runtime/native_list.hpp", "HE_CPP_REQ_NATIVE_LIST", "Managed-style list abstraction support."),
                Make("NativeDictionary", "runtime/native_dictionary.hpp", "HE_CPP_REQ_NATIVE_DICTIONARY", "Managed-style dictionary abstraction support."),
                Make("NativeDisposable", "runtime/native_disposable.hpp", "HE_CPP_REQ_NATIVE_DISPOSABLE", "Managed-style disposable contract support."),
                Make("NativeEquatable", "runtime/native_equatable.hpp", "HE_CPP_REQ_NATIVE_EQUATABLE", "Managed-style equatable contract support."),
                Make("NativeCast", "runtime/native_cast.hpp", "HE_CPP_REQ_NATIVE_CAST", "Runtime-assisted safe cast support for declaration-pattern lowering."),
                Make("BinaryReader", "system/io/binary-reader.hpp", "HE_CPP_REQ_BINARY_READER", "Binary reader support for serialized engine data."),
                Make("BinaryWriter", "system/io/binary-writer.hpp", "HE_CPP_REQ_BINARY_WRITER", "Binary writer support for serialized engine data."),
                Make("Stream", "system/io/stream.hpp", "HE_CPP_REQ_STREAM", "Stream abstraction support for runtime IO."),
                Make("File", "system/io/file.hpp", "HE_CPP_REQ_FILE", "File abstraction support for host-backed IO.")
            };
        }

        static CPPRuntimeRequirementDefinition Make(string name, string includePath, string configDefineName, string description) {
            return new CPPRuntimeRequirementDefinition {
                Name = name,
                IncludePath = includePath,
                ConfigDefineName = configDefineName,
                Description = description
            };
        }
    }
}
