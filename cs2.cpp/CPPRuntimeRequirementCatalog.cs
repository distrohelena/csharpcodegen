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
                Make("NativeSpan", "runtime/native_span.hpp", "HE_CPP_REQ_NATIVE_SPAN", "Managed-style non-owning span view support for transient buffer access."),
                Make("NativeNullable", "runtime/native_nullable.hpp", "HE_CPP_REQ_NATIVE_NULLABLE", "Managed-style nullable value wrapper support."),
                Make("NativeString", "runtime/native_string.hpp", "HE_CPP_REQ_NATIVE_STRING", "Managed-style string abstraction support."),
                Make("NativeList", "runtime/native_list.hpp", "HE_CPP_REQ_NATIVE_LIST", "Managed-style list abstraction support."),
                Make("NativeStack", "runtime/native_stack.hpp", "HE_CPP_REQ_NATIVE_STACK", "Managed-style stack abstraction support for lightweight LIFO state."),
                Make("NativeDictionary", "runtime/native_dictionary.hpp", "HE_CPP_REQ_NATIVE_DICTIONARY", "Managed-style dictionary abstraction support."),
                Make("NativeTuple", "runtime/native_tuple.hpp", "HE_CPP_REQ_NATIVE_TUPLE", "Lightweight managed tuple support for transpiled value-tuple data flow."),
                Make("NativeDisposable", "runtime/native_disposable.hpp", "HE_CPP_REQ_NATIVE_DISPOSABLE", "Managed-style disposable contract support."),
                Make("NativeEquatable", "runtime/native_equatable.hpp", "HE_CPP_REQ_NATIVE_EQUATABLE", "Managed-style equatable contract support."),
                Make("NativeEvent", "runtime/native_event.hpp", "HE_CPP_REQ_NATIVE_EVENT", "Managed-style event contract support for transpiled event members."),
                Make("NativeDateTime", "runtime/native_datetime.hpp", "HE_CPP_REQ_NATIVE_DATETIME", "Lightweight managed DateTime and TimeSpan support for engine timing and timestamps."),
                Make("NativeType", "runtime/native_type.hpp", "HE_CPP_REQ_NATIVE_TYPE", "Lightweight managed type-token support without reflection."),
                Make("NativeCast", "runtime/native_cast.hpp", "HE_CPP_REQ_NATIVE_CAST", "Runtime-assisted safe cast support for declaration-pattern lowering."),
                Make("StringBuilder", "system/text/string-builder.hpp", "HE_CPP_REQ_STRING_BUILDER", "Lightweight string builder support for append-heavy managed text composition.", CPPFeatureKind.DebugOverlay, CPPFeatureKind.Shaders),
                Make("Regex", "system/text/regular_expressions/regex.hpp", "HE_CPP_REQ_REGEX", "Lightweight regex, match, and named-group support for transpiled managed text parsing.", CPPFeatureKind.Shaders),
                Make("BinaryReader", "system/io/binary-reader.hpp", "HE_CPP_REQ_BINARY_READER", "Binary reader support for serialized engine data."),
                Make("BinaryWriter", "system/io/binary-writer.hpp", "HE_CPP_REQ_BINARY_WRITER", "Binary writer support for serialized engine data."),
                Make("Stream", "system/io/stream.hpp", "HE_CPP_REQ_STREAM", "Stream abstraction support for runtime IO."),
                Make("StreamReader", "system/io/stream-reader.hpp", "HE_CPP_REQ_STREAM_READER", "Stream reader support for direct UTF-8 text reads from runtime streams."),
                Make("StringReader", "system/io/string-reader.hpp", "HE_CPP_REQ_STRING_READER", "String reader support for line-based text iteration without heap-heavy stream wrappers.", CPPFeatureKind.Shaders),
                Make("MemoryStream", "system/io/memory-stream.hpp", "HE_CPP_REQ_MEMORY_STREAM", "Memory stream abstraction support for transient in-memory IO."),
                Make("FileStream", "system/io/file-stream.hpp", "HE_CPP_REQ_FILE_STREAM", "File stream abstraction support for host-backed runtime IO."),
                Make("File", "system/io/file.hpp", "HE_CPP_REQ_FILE", "File abstraction support for host-backed IO.")
            };
        }

        static CPPRuntimeRequirementDefinition Make(string name, string includePath, string configDefineName, string description, params CPPFeatureKind[] owningFeatures) {
            CPPRuntimeRequirementDefinition definition = new CPPRuntimeRequirementDefinition {
                Name = name,
                IncludePath = includePath,
                ConfigDefineName = configDefineName,
                Description = description
            };

            if (owningFeatures != null) {
                foreach (CPPFeatureKind owningFeature in owningFeatures) {
                    definition.OwningFeatures.Add(owningFeature);
                }
            }

            return definition;
        }
    }
}
