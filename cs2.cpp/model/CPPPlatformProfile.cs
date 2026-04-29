namespace cs2.cpp {
    /// <summary>
    /// Describes platform-level constraints that influence portable C++ output.
    /// </summary>
    public class CPPPlatformProfile {
        /// <summary>
        /// Gets or sets the target platform kind.
        /// </summary>
        public CPPPlatformKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the display name used in reports and metadata.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the preprocessor symbol emitted for the selected platform.
        /// </summary>
        public string DefineName { get; set; }

        /// <summary>
        /// Gets or sets whether the platform uses little-endian memory layout.
        /// </summary>
        public bool IsLittleEndian { get; set; }

        /// <summary>
        /// Gets or sets whether the platform is the first-party Windows development host.
        /// </summary>
        public bool IsWindowsHost { get; set; }

        /// <summary>
        /// Creates the default Windows headless development profile.
        /// </summary>
        /// <returns>The default Windows platform profile.</returns>
        public static CPPPlatformProfile CreateWindowsHeadless() {
            return new CPPPlatformProfile {
                Kind = CPPPlatformKind.WindowsHeadless,
                Name = "windows-headless",
                DefineName = "HE_CPP_PLATFORM_WINDOWS",
                IsLittleEndian = true,
                IsWindowsHost = true
            };
        }
    }
}
