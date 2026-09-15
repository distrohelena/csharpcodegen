using cs2.core;

namespace cs2.cpp {
    public class CPPConversionRules : ConversionRules {
        /// <summary>
        /// Gets or sets whether generated managed strings use standard-library storage.
        /// </summary>
        public bool UseStdString { get; set; } = true;

        /// <summary>
        /// Gets or sets whether shared list runtime helpers use standard-library vector storage.
        /// </summary>
        public bool UseStdVector { get; set; } = true;

        /// <summary>
        /// Gets or sets whether shared dictionary runtime helpers use standard-library unordered-map storage.
        /// </summary>
        public bool UseStdUnorderedMap { get; set; } = true;

        /// <summary>
        /// Gets or sets whether generated code may use C++ exception unwinding.
        /// </summary>
        public bool UseExceptions { get; set; } = true;

        /// <summary>
        /// Gets or sets whether generated code may use compiler RTTI for runtime type checks.
        /// </summary>
        public bool UseRtti { get; set; } = true;

        /// <summary>
        /// Gets or sets the provider header used by restricted runtime storage and failure helpers.
        /// </summary>
        public string RuntimeProviderHeader { get; set; } = string.Empty;
    }
}
