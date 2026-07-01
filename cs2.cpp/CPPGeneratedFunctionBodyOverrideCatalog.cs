using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Owns backend-emitter substitutions for generated function bodies.
    /// </summary>
    public sealed class CPPGeneratedFunctionBodyOverrideCatalog {
        /// <summary>
        /// Writes a specialized generated function body when the active conversion settings require one.
        /// </summary>
        /// <param name="options">Active conversion options.</param>
        /// <param name="conversionClass">Converted class that owns the generated function.</param>
        /// <param name="function">Generated function being emitted.</param>
        /// <param name="writer">Writer that receives the specialized body.</param>
        /// <returns><c>true</c> when a specialized body was emitted; otherwise <c>false</c>.</returns>
        public bool TryWriteOverride(CPPConversionOptions options, ConversionClass conversionClass, ConversionFunction function, TextWriter writer) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            } else if (conversionClass == null) {
                throw new ArgumentNullException(nameof(conversionClass));
            } else if (function == null) {
                throw new ArgumentNullException(nameof(function));
            } else if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }

            return false;
        }
    }
}
