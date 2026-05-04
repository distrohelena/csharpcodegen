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

        /// <summary>
        /// Creates the default GameCube headless profile.
        /// </summary>
        /// <returns>The default GameCube platform profile.</returns>
        public static CPPPlatformProfile CreateGameCubeHeadless() {
            return new CPPPlatformProfile {
                Kind = CPPPlatformKind.GameCubeHeadless,
                Name = "gamecube-headless",
                DefineName = "HE_CPP_PLATFORM_GAMECUBE",
                IsLittleEndian = false,
                IsWindowsHost = false
            };
        }

        /// <summary>
        /// Creates the default PlayStation 2 headless profile.
        /// </summary>
        /// <returns>The default PlayStation 2 platform profile.</returns>
        public static CPPPlatformProfile CreatePlayStation2Headless() {
            return new CPPPlatformProfile {
                Kind = CPPPlatformKind.PlayStation2Headless,
                Name = "ps2-headless",
                DefineName = "HE_CPP_PLATFORM_PS2",
                IsLittleEndian = true,
                IsWindowsHost = false
            };
        }

        /// <summary>
        /// Creates the default Nintendo 64 headless profile.
        /// </summary>
        /// <returns>The default Nintendo 64 platform profile.</returns>
        public static CPPPlatformProfile CreateNintendo64Headless() {
            return new CPPPlatformProfile {
                Kind = CPPPlatformKind.Nintendo64Headless,
                Name = "n64-headless",
                DefineName = "HE_CPP_PLATFORM_N64",
                IsLittleEndian = false,
                IsWindowsHost = false
            };
        }
    }
}
