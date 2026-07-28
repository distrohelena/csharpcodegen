namespace cs2.core {
    /// <summary>
    /// Identifies one maintained C# declaration so generated native output can retain an authoritative connection to its source code.
    /// </summary>
    public class ConversionSourceLocation {
        /// <summary>
        /// Gets the compilation assembly that owns the maintained C# declaration.
        /// </summary>
        public string AssemblyName { get; }

        /// <summary>
        /// Gets the stable Roslyn symbol display that identifies the maintained C# declaration within its assembly.
        /// </summary>
        public string MaintainedSymbol { get; }

        /// <summary>
        /// Gets the normalized absolute path of the maintained C# declaration file.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Gets the one-based line number on which the maintained C# declaration begins.
        /// </summary>
        public int LineNumber { get; }

        /// <summary>
        /// Initializes a complete maintained C# source identity.
        /// </summary>
        /// <param name="assemblyName">Compilation assembly that owns the declaration.</param>
        /// <param name="maintainedSymbol">Stable Roslyn symbol display for the declaration.</param>
        /// <param name="filePath">Path of the declaration file.</param>
        /// <param name="lineNumber">One-based declaration line number.</param>
        public ConversionSourceLocation(string assemblyName, string maintainedSymbol, string filePath, int lineNumber) {
            if (string.IsNullOrWhiteSpace(assemblyName)) {
                throw new ArgumentException("A maintained source location requires a compilation assembly name.", nameof(assemblyName));
            }

            if (string.IsNullOrWhiteSpace(maintainedSymbol)) {
                throw new ArgumentException("A maintained source location requires a stable source symbol.", nameof(maintainedSymbol));
            }

            if (string.IsNullOrWhiteSpace(filePath)) {
                throw new ArgumentException("A maintained source location requires a source file path.", nameof(filePath));
            }

            if (lineNumber <= 0) {
                throw new ArgumentOutOfRangeException(nameof(lineNumber), lineNumber, "A maintained source location requires a one-based line number.");
            }

            AssemblyName = assemblyName;
            MaintainedSymbol = maintainedSymbol;
            FilePath = Path.GetFullPath(filePath);
            LineNumber = lineNumber;
        }
    }
}
