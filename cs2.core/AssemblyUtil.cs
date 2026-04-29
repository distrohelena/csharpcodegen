using System.Reflection;

namespace cs2.core {
    /// <summary>
    /// Resolves assembly-related paths used by converter runtime assets.
    /// </summary>
    public static class AssemblyUtil {
        /// <summary>
        /// Gets the folder that contains the current entry assembly, or the base directory when no entry assembly is available.
        /// </summary>
        /// <returns>The startup folder used to locate converter runtime assets.</returns>
        public static string GetStartFolder() {
            string? entryAssemblyLocation = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(entryAssemblyLocation)) {
                string? startFolder = Path.GetDirectoryName(entryAssemblyLocation);
                if (!string.IsNullOrWhiteSpace(startFolder)) {
                    return startFolder;
                }
            }

            return AppContext.BaseDirectory;
        }
    }
}
