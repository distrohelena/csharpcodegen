namespace cs2.cpp {
    /// <summary>
    /// Identifies the runtime platform the generated core is intended to run on.
    /// </summary>
    public enum CPPPlatformKind {
        /// <summary>
        /// Headless Windows host used for initial development.
        /// </summary>
        WindowsHeadless,

        /// <summary>
        /// Headless Nintendo 64 target.
        /// </summary>
        Nintendo64Headless,

        /// <summary>
        /// Headless PlayStation 2 target.
        /// </summary>
        PlayStation2Headless,

        /// <summary>
        /// Headless Nintendo DS target.
        /// </summary>
        NintendoDsHeadless,

        /// <summary>
        /// Headless caller-defined custom native target.
        /// </summary>
        CustomHeadless,

        /// <summary>
        /// Headless PlayStation Portable target.
        /// </summary>
        PspHeadless
    }
}
