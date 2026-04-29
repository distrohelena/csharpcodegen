namespace cs2.cpp {
    /// <summary>
    /// Indicates the severity of a C++ conversion diagnostic.
    /// </summary>
    public enum CPPDiagnosticSeverity {
        /// <summary>
        /// Informational diagnostic that does not block output.
        /// </summary>
        Info,

        /// <summary>
        /// Warning diagnostic that highlights a risky or incomplete conversion.
        /// </summary>
        Warning,

        /// <summary>
        /// Error diagnostic that blocks trustworthy output.
        /// </summary>
        Error
    }
}
