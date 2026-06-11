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
        /// Gets or sets the generated runtime math convention required by the target platform.
        /// </summary>
        public CPPGeneratedMathConventionKind GeneratedMathConvention { get; set; }

        /// <summary>
        /// Gets or sets the native pointer size, in bytes, used when computing unmanaged value-type layout.
        /// </summary>
        public int PointerSizeInBytes { get; set; }

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
                IsWindowsHost = true,
                GeneratedMathConvention = CPPGeneratedMathConventionKind.EngineRowVector,
                PointerSizeInBytes = 8
            };
        }

        /// <summary>
        /// Creates one caller-defined custom native profile from generic platform-shape metadata.
        /// </summary>
        /// <param name="platformId">Caller-owned platform identifier used for generated config naming.</param>
        /// <param name="isLittleEndian">Whether the target uses little-endian memory layout.</param>
        /// <param name="generatedMathConvention">Generated runtime math convention required by the target.</param>
        /// <param name="pointerSizeInBytes">Native pointer size used by the target runtime.</param>
        /// <returns>The resolved custom platform profile.</returns>
        public static CPPPlatformProfile CreateCustomHeadless(
            string platformId,
            bool isLittleEndian,
            CPPGeneratedMathConventionKind generatedMathConvention,
            int pointerSizeInBytes) {
            if (string.IsNullOrWhiteSpace(platformId)) {
                throw new ArgumentException("Platform id must not be empty.", nameof(platformId));
            } else if (pointerSizeInBytes <= 0) {
                throw new ArgumentOutOfRangeException(nameof(pointerSizeInBytes), "Pointer size must be positive.");
            }

            return new CPPPlatformProfile {
                Kind = CPPPlatformKind.CustomHeadless,
                Name = $"{platformId}-headless",
                DefineName = $"HE_CPP_PLATFORM_{platformId.ToUpperInvariant()}",
                IsLittleEndian = isLittleEndian,
                IsWindowsHost = false,
                GeneratedMathConvention = generatedMathConvention,
                PointerSizeInBytes = pointerSizeInBytes
            };
        }

        /// <summary>
        /// Creates the default Nintendo DS headless profile.
        /// </summary>
        /// <returns>The default Nintendo DS platform profile.</returns>
        public static CPPPlatformProfile CreateNintendoDsHeadless() {
            return new CPPPlatformProfile {
                Kind = CPPPlatformKind.NintendoDsHeadless,
                Name = "ds-headless",
                DefineName = "HE_CPP_PLATFORM_DS",
                IsLittleEndian = true,
                IsWindowsHost = false,
                GeneratedMathConvention = CPPGeneratedMathConventionKind.EngineRowVector,
                PointerSizeInBytes = 4
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
                IsWindowsHost = false,
                GeneratedMathConvention = CPPGeneratedMathConventionKind.EngineRowVector,
                PointerSizeInBytes = 4
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
                IsWindowsHost = false,
                GeneratedMathConvention = CPPGeneratedMathConventionKind.EngineRowVector,
                PointerSizeInBytes = 4
            };
        }
    }
}
