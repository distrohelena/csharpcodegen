namespace cs2.cpp {
    /// <summary>
    /// Bundles the resolved profiles that define a named C++ conversion preset.
    /// </summary>
    public class CPPConversionPreset {
        /// <summary>
        /// Gets or sets the stable preset identifier.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the compiler profile used by the preset.
        /// </summary>
        public CPPCompilerProfile CompilerProfile { get; set; } = CPPCompilerProfile.CreateMsvc();

        /// <summary>
        /// Gets or sets the platform profile used by the preset.
        /// </summary>
        public CPPPlatformProfile PlatformProfile { get; set; } = CPPPlatformProfile.CreateWindowsHeadless();

        /// <summary>
        /// Gets or sets the runtime profile used by the preset.
        /// </summary>
        public CPPRuntimeProfile RuntimeProfile { get; set; } = CPPRuntimeProfile.CreateStlLite();

        /// <summary>
        /// Gets or sets the feature profile used by the preset.
        /// </summary>
        public CPPBuildFeatureProfile BuildFeatureProfile { get; set; } = CPPBuildFeatureProfile.CreateDefault();

        /// <summary>
        /// Gets or sets the restriction profile used by the preset.
        /// </summary>
        public CPPRestrictionProfile RestrictionProfile { get; set; } = CPPRestrictionProfile.CreatePermissive(string.Empty);
    }
}
