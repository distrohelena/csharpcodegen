using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Describes one direct Tracy scope emitted for a maintained C# declaration.
    /// </summary>
    public class CPPGeneratedFunctionProfilingScope {
        /// <summary>
        /// Gets the C++ source file that contains the emitted scope.
        /// </summary>
        public string GeneratedFilePath { get; }

        /// <summary>
        /// Gets the maintained declaration source identity attached to the emitted scope.
        /// </summary>
        public ConversionSourceLocation SourceLocation { get; }

        /// <summary>
        /// Initializes one manifest-ready generated profiling scope.
        /// </summary>
        /// <param name="generatedFilePath">C++ source file receiving the scope.</param>
        /// <param name="sourceLocation">Maintained C# declaration identity represented by the scope.</param>
        public CPPGeneratedFunctionProfilingScope(string generatedFilePath, ConversionSourceLocation sourceLocation) {
            if (string.IsNullOrWhiteSpace(generatedFilePath)) {
                throw new ArgumentException("A profiling scope requires its generated source file path.", nameof(generatedFilePath));
            }

            if (sourceLocation == null) {
                throw new ArgumentNullException(nameof(sourceLocation));
            }

            GeneratedFilePath = generatedFilePath;
            SourceLocation = sourceLocation;
        }
    }
}
