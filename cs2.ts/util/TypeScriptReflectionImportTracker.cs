namespace cs2.ts.util {
    /// <summary>
    /// Tracks whether reflection runtime imports are required for the current output.
    /// </summary>
    public class TypeScriptReflectionImportTracker {
        /// <summary>
        /// Gets or sets whether type registration imports are required.
        /// </summary>
        public bool NeedsTypeImport { get; set; }

        /// <summary>
        /// Gets or sets whether enum registration imports are required.
        /// </summary>
        public bool NeedsEnumImport { get; set; }

        /// <summary>
        /// Gets or sets whether metadata registration imports are required.
        /// </summary>
        public bool NeedsMetadataImport { get; set; }

        /// <summary>
        /// Clears all tracked import flags.
        /// </summary>
        public void Reset() {
            NeedsTypeImport = false;
            NeedsEnumImport = false;
            NeedsMetadataImport = false;
        }
    }
}
