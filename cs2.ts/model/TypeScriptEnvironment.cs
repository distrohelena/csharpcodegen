namespace cs2.ts {
    /// <summary>
    /// Enumerates the supported runtime environments for generated TypeScript output.
    /// </summary>
    public enum TypeScriptEnvironment {
        /// <summary>
        /// Targets a Node.js runtime and its available APIs.
        /// </summary>
        NodeJS,
        /// <summary>
        /// Targets a browser runtime and its available APIs.
        /// </summary>
        Web
    }
}
