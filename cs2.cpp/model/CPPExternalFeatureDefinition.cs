namespace cs2.cpp {
    /// <summary>
    /// Describes one caller-owned feature definition consumed by the generic C#-to-C++ converter.
    /// </summary>
    public sealed class CPPExternalFeatureDefinition {
        /// <summary>
        /// Initializes a new external feature definition.
        /// </summary>
        /// <param name="id">Free-form caller-owned feature id.</param>
        /// <param name="defaultMode">Default mode declared in metadata.</param>
        /// <param name="conflictPolicy">Conflict policy declared in metadata.</param>
        public CPPExternalFeatureDefinition(string id, CPPFeatureMode defaultMode, CPPFeatureConflictPolicy conflictPolicy) {
            Id = id;
            DefaultMode = defaultMode;
            ConflictPolicy = conflictPolicy;
        }

        /// <summary>
        /// Gets the free-form caller-owned feature id.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the default mode declared by metadata.
        /// </summary>
        public CPPFeatureMode DefaultMode { get; }

        /// <summary>
        /// Gets the conflict policy declared by metadata.
        /// </summary>
        public CPPFeatureConflictPolicy ConflictPolicy { get; }
    }
}
