namespace cs2.cpp {
    /// <summary>
    /// Collects generated function profiling scopes for one conversion output so the emitted manifest is authoritative.
    /// </summary>
    public class CPPGeneratedFunctionProfilingManifest {
        readonly List<CPPGeneratedFunctionProfilingScope> Scopes;

        /// <summary>
        /// Initializes an empty scope collection for one conversion run.
        /// </summary>
        public CPPGeneratedFunctionProfilingManifest() {
            Scopes = new List<CPPGeneratedFunctionProfilingScope>();
        }

        /// <summary>
        /// Gets the scopes emitted during the current conversion run.
        /// </summary>
        public IReadOnlyList<CPPGeneratedFunctionProfilingScope> Entries => Scopes;

        /// <summary>
        /// Records one scope that has been written into generated C++ source.
        /// </summary>
        /// <param name="generatedFilePath">C++ source file containing the scope.</param>
        /// <param name="sourceLocation">Maintained C# declaration represented by the scope.</param>
        public void Add(string generatedFilePath, cs2.core.ConversionSourceLocation sourceLocation) {
            Scopes.Add(new CPPGeneratedFunctionProfilingScope(generatedFilePath, sourceLocation));
        }

        /// <summary>
        /// Removes scopes from a previous output pass so repeated conversions do not leak profiling manifest entries.
        /// </summary>
        public void Clear() {
            Scopes.Clear();
        }
    }
}
