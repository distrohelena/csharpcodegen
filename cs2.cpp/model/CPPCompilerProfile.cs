namespace cs2.cpp {
    /// <summary>
    /// Describes compiler-specific code generation constraints and feature toggles.
    /// </summary>
    public class CPPCompilerProfile {
        /// <summary>
        /// Gets or sets the compiler family.
        /// </summary>
        public CPPCompilerKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the display name used in reports and generated metadata.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the preprocessor symbol emitted for the selected compiler.
        /// </summary>
        public string DefineName { get; set; }

        /// <summary>
        /// Gets or sets whether the compiler supports force-inline style attributes through the runtime layer.
        /// </summary>
        public bool SupportsForceInline { get; set; }

        /// <summary>
        /// Gets or sets whether pragma-once headers are allowed for this compiler.
        /// </summary>
        public bool SupportsPragmaOnce { get; set; }

        /// <summary>
        /// Creates the default MSVC profile used for the first Windows milestone.
        /// </summary>
        /// <returns>The default MSVC compiler profile.</returns>
        public static CPPCompilerProfile CreateMsvc() {
            return new CPPCompilerProfile {
                Kind = CPPCompilerKind.Msvc,
                Name = "msvc",
                DefineName = "HE_CPP_COMPILER_MSVC",
                SupportsForceInline = true,
                SupportsPragmaOnce = true
            };
        }

        /// <summary>
        /// Creates the default GCC profile for portability work.
        /// </summary>
        /// <returns>The default GCC compiler profile.</returns>
        public static CPPCompilerProfile CreateGcc() {
            return new CPPCompilerProfile {
                Kind = CPPCompilerKind.Gcc,
                Name = "gcc",
                DefineName = "HE_CPP_COMPILER_GCC",
                SupportsForceInline = true,
                SupportsPragmaOnce = true
            };
        }
    }
}
