namespace cs2.cpp {
    /// <summary>
    /// Defines which runtime facilities are forbidden for a conversion preset.
    /// </summary>
    public class CPPRestrictionProfile {
        /// <summary>
        /// Gets or sets the stable restriction profile name written into reports.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether shader systems are forbidden.
        /// </summary>
        public bool ForbidShaders { get; set; }

        /// <summary>
        /// Gets or sets whether runtime JSON parsing systems are forbidden.
        /// </summary>
        public bool ForbidRuntimeJson { get; set; }

        /// <summary>
        /// Gets or sets whether reflection-like runtime support is forbidden.
        /// </summary>
        public bool ForbidReflectionLikeRuntime { get; set; }

        /// <summary>
        /// Gets or sets whether regex helpers are forbidden.
        /// </summary>
        public bool ForbidRegex { get; set; }

        /// <summary>
        /// Gets or sets whether debug-only systems are forbidden.
        /// </summary>
        public bool ForbidDebugOnlySystems { get; set; }

        /// <summary>
        /// Creates a permissive restriction profile that allows all currently modeled runtime systems.
        /// </summary>
        /// <param name="name">Stable profile name to assign to the permissive restriction set.</param>
        /// <returns>The permissive restriction profile.</returns>
        public static CPPRestrictionProfile CreatePermissive(string name) {
            return new CPPRestrictionProfile {
                Name = name ?? string.Empty
            };
        }
    }
}
