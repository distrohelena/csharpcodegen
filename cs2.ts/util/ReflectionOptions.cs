namespace cs2.ts.util {
    /// <summary>
    /// Configures reflection metadata emission for the TypeScript backend.
    /// </summary>
    public sealed class ReflectionOptions {
        /// <summary>
        /// Provides a shared default reflection options instance.
        /// </summary>
        public static readonly ReflectionOptions Default = new ReflectionOptions();

        /// <summary>
        /// Gets or sets whether reflection metadata and registration calls are emitted.
        /// </summary>
        public bool EnableReflection { get; set; } = true;

        /// <summary>
        /// Gets or sets whether registration calls are emitted as private static fields for caching.
        /// </summary>
        public bool UseStaticReflectionCache { get; set; } = true;

        /// <summary>
        /// Gets or sets the private static field name used for cached type metadata.
        /// </summary>
        public string PrivateStaticFieldName { get; set; } = "__type";

        /// <summary>
        /// Gets or sets the runtime helper identifier used to register types.
        /// </summary>
        public string RegisterTypeIdent { get; set; } = "registerType";

        /// <summary>
        /// Gets or sets the runtime helper identifier used to register enums.
        /// </summary>
        public string RegisterEnumIdent { get; set; } = "registerEnum";

        /// <summary>
        /// Gets or sets the runtime helper identifier used to register metadata-only entries.
        /// </summary>
        public string RegisterMetadataIdent { get; set; } = "registerMetadata";

        /// <summary>
        /// Gets or sets the module path used for reflection runtime imports.
        /// </summary>
        public string RuntimeImportModule { get; set; } = "./src/index";

        /// <summary>
        /// Creates a copy of the options so callers can adjust settings without mutating shared instances.
        /// </summary>
        /// <returns>A copy of the current reflection options.</returns>
        public ReflectionOptions Clone() {
            return new ReflectionOptions {
                EnableReflection = EnableReflection,
                UseStaticReflectionCache = UseStaticReflectionCache,
                PrivateStaticFieldName = PrivateStaticFieldName,
                RegisterTypeIdent = RegisterTypeIdent,
                RegisterEnumIdent = RegisterEnumIdent,
                RegisterMetadataIdent = RegisterMetadataIdent,
                RuntimeImportModule = RuntimeImportModule
            };
        }
    }
}
