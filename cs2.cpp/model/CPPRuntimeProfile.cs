namespace cs2.cpp {
    /// <summary>
    /// Describes which runtime conveniences generated C++ code may rely on.
    /// </summary>
    public class CPPRuntimeProfile {
        /// <summary>
        /// Gets or sets the runtime support level.
        /// </summary>
        public CPPRuntimeKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the display name used in reports and manifests.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the preprocessor symbol emitted for the selected runtime profile.
        /// </summary>
        public string DefineName { get; set; }

        /// <summary>
        /// Gets or sets whether generated code may use std::string through the runtime layer.
        /// </summary>
        public bool UseStdString { get; set; }

        /// <summary>
        /// Gets or sets whether generated code may use std::vector through the runtime layer.
        /// </summary>
        public bool UseStdVector { get; set; }

        /// <summary>
        /// Gets or sets whether generated code may use unordered-map style containers.
        /// </summary>
        public bool UseStdUnorderedMap { get; set; }

        /// <summary>
        /// Gets or sets whether generated code may use unordered-set style containers.
        /// </summary>
        public bool UseStdUnorderedSet { get; set; }

        /// <summary>Gets or sets whether delegates use the standard function wrapper.</summary>
        public bool UseStdFunction { get; set; } = true;

        /// <summary>Gets or sets whether stopwatch timing uses the standard monotonic clock.</summary>
        public bool UseStdChrono { get; set; } = true;

        /// <summary>Gets or sets whether shared span storage uses standard shared ownership.</summary>
        public bool UseStdSharedPtr { get; set; } = true;

        /// <summary>Gets or sets whether numeric helpers use the standard C++ math library.</summary>
        public bool UseStdMath { get; set; } = true;

        /// <summary>Gets or sets whether file streams may access the host file system.</summary>
        public bool UseHostedFileSystem { get; set; } = true;

        /// <summary>
        /// Gets or sets whether generated code may depend on C++ exceptions.
        /// </summary>
        public bool UseExceptions { get; set; }

        /// <summary>
        /// Gets or sets whether generated code may depend on RTTI. Generated dispatch has always assumed RTTI, so a
        /// target that compiles without it must opt out explicitly through the generic option.
        /// </summary>
        public bool UseRtti { get; set; } = true;

        /// <summary>
        /// Creates the default STL-lite runtime profile.
        /// </summary>
        /// <returns>The default STL-lite runtime profile.</returns>
        public static CPPRuntimeProfile CreateStlLite() {
            return new CPPRuntimeProfile {
                Kind = CPPRuntimeKind.StlLite,
                Name = "stl-lite",
                DefineName = "HE_CPP_RUNTIME_STL_LITE",
                UseStdString = true,
                UseStdVector = true,
                UseStdUnorderedMap = true,
                UseStdUnorderedSet = true,
                UseExceptions = false,
                UseRtti = true
            };
        }

        /// <summary>
        /// Creates the custom retro runtime profile for low-footprint console builds.
        /// </summary>
        /// <returns>The custom retro runtime profile.</returns>
        public static CPPRuntimeProfile CreateCustomRetro() {
            return new CPPRuntimeProfile {
                Kind = CPPRuntimeKind.CustomRetro,
                Name = "custom-retro",
                DefineName = "HE_CPP_RUNTIME_CUSTOM_RETRO",
                UseStdString = false,
                UseStdVector = false,
                UseStdUnorderedMap = false,
                UseStdUnorderedSet = false,
                UseExceptions = false,
                UseRtti = true
            };
        }
    }
}
