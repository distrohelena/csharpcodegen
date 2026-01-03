using System.Collections.Generic;
using System.Linq;

namespace cs2.go.util {
    /// <summary>
    /// Tracks Go package imports needed for generated output.
    /// </summary>
    public class GoImportTracker {
        /// <summary>
        /// Initializes a new import tracker.
        /// </summary>
        public GoImportTracker() {
            Imports = new Dictionary<string, GoImportDefinition>();
        }

        /// <summary>
        /// Gets the registered imports keyed by path.
        /// </summary>
        Dictionary<string, GoImportDefinition> Imports { get; }

        /// <summary>
        /// Gets a snapshot of the current imports.
        /// </summary>
        public IReadOnlyList<GoImportDefinition> CurrentImports => Imports.Values.ToList();

        /// <summary>
        /// Clears all tracked imports.
        /// </summary>
        public void Reset() {
            Imports.Clear();
        }

        /// <summary>
        /// Registers a new import entry.
        /// </summary>
        /// <param name="path">The import path.</param>
        /// <param name="alias">Optional alias.</param>
        public void AddImport(string path, string alias = "") {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            if (!Imports.ContainsKey(path)) {
                Imports.Add(path, new GoImportDefinition(path, alias));
            }
        }

        /// <summary>
        /// Returns whether any imports have been registered.
        /// </summary>
        /// <returns>True when there are imports to emit.</returns>
        public bool HasImports() {
            return Imports.Count > 0;
        }
    }
}
