using System;

namespace cs2.ts.util {
    /// <summary>
    /// Describes a runtime metadata extraction request for the TypeScript runtime.
    /// </summary>
    public class TypeScriptRuntimeMetadataRequest {
        /// <summary>
        /// Gets or sets the .net.ts runtime directory to inspect.
        /// </summary>
        public string RuntimeDirectory { get; set; }

        /// <summary>
        /// Gets or sets whether missing TypeScript dependencies should be installed.
        /// </summary>
        public bool EnsureDependencies { get; set; }

        /// <summary>
        /// Gets or sets whether failures should throw exceptions.
        /// </summary>
        public bool ThrowOnError { get; set; }

        /// <summary>
        /// Gets or sets whether output should be forwarded to the logger.
        /// </summary>
        public bool ForwardOutput { get; set; }

        /// <summary>
        /// Gets or sets the timeout in minutes for dependency installation.
        /// </summary>
        public int InstallTimeoutMinutes { get; set; } = 3;

        /// <summary>
        /// Gets or sets the logger used for warnings and status messages.
        /// </summary>
        public Action<string> Logger { get; set; }
    }
}
