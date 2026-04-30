namespace cs2.cpp {
    /// <summary>
    /// Identifies a prunable engine capability in the generated C++ build.
    /// </summary>
    public enum CPPFeatureKind {
        /// <summary>
        /// Identifies the shared 2D rendering foundation.
        /// </summary>
        Render2D,

        /// <summary>
        /// Identifies sprite rendering and sprite-facing engine systems.
        /// </summary>
        Sprites,

        /// <summary>
        /// Identifies text rendering and font-facing engine systems.
        /// </summary>
        Text2D,

        /// <summary>
        /// Identifies shader authoring, packaging, and runtime shader systems.
        /// </summary>
        Shaders,

        /// <summary>
        /// Identifies debug overlay rendering and related diagnostics UI.
        /// </summary>
        DebugOverlay,
    }
}
