using cs2.core;

namespace cs2.go {
    /// <summary>
    /// Conversion rules specific to the Go backend.
    /// </summary>
    public class GoConversionRules : ConversionRules {
        /// <summary>
        /// Gets or sets whether reference types should be emitted as pointers.
        /// </summary>
        public bool UsePointersForReferenceTypes { get; set; } = true;
    }
}
