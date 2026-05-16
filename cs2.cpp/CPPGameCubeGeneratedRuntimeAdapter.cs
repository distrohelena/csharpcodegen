namespace cs2.cpp {
    /// <summary>
    /// Applies GameCube-specific post-generation runtime support rewrites required by disc-backed packaged builds.
    /// </summary>
    public sealed class CPPGameCubeGeneratedRuntimeAdapter {
        /// <summary>
        /// Applies GameCube runtime support rewrites to one generated output root.
        /// </summary>
        /// <param name="outputFolder">Generated output folder that contains the converted native core.</param>
        public void Apply(string outputFolder) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            AdaptGeneratedFileIo(Path.Combine(outputFolder, "system", "io", "file.cpp"));
        }

        /// <summary>
        /// Rewrites generated native file support so packaged GameCube runtime paths are dispatched through the platform DVD-backed file system instead of host-style <c>fopen</c>.
        /// </summary>
        /// <param name="filePath">Generated native file source path.</param>
        static void AdaptGeneratedFileIo(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                throw new ArgumentException("File path must not be empty.", nameof(filePath));
            } else if (!File.Exists(filePath)) {
                return;
            }

            string contents = File.ReadAllText(filePath);
            if (contents.Contains("GameCubeDiscFileSystem", StringComparison.Ordinal)) {
                return;
            }

            string newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string updatedContents = contents;
            if (!updatedContents.Contains("#include \"platform/gamecube/GameCubeDiscFileSystem.hpp\"", StringComparison.Ordinal)) {
                updatedContents = updatedContents.Replace(
                    "#include \"file.hpp\"",
                    "#include \"file.hpp\"" + newline + "#include \"platform/gamecube/GameCubeDiscFileSystem.hpp\"",
                    StringComparison.Ordinal);
            }

            updatedContents = updatedContents.Replace(
                "bool File::Exists(const char* fileName) {" + newline
                + "\tif (!fileName)" + newline
                + "\t{" + newline
                + "\t\treturn false;" + newline
                + "\t}" + newline + newline
                + "\tstd::ifstream file(fileName);" + newline
                + "\treturn file.good();" + newline
                + "}",
                "bool File::Exists(const char* fileName) {" + newline
                + "\tif (!fileName)" + newline
                + "\t{" + newline
                + "\t\treturn false;" + newline
                + "\t}" + newline + newline
                + "\tif (helengine::gamecube::GameCubeDiscFileSystem::CanHandlePath(fileName)) {" + newline
                + "\t\treturn helengine::gamecube::GameCubeDiscFileSystem::Exists(fileName);" + newline
                + "\t}" + newline + newline
                + "\tstd::ifstream file(fileName);" + newline
                + "\treturn file.good();" + newline
                + "}",
                StringComparison.Ordinal);

            updatedContents = updatedContents.Replace(
                "FileStream* File::OpenRead(const char* filePath)" + newline
                + "{" + newline
                + "\treturn new FileStream(filePath, FileMode::Open, FileAccess::Read, FileShare::Read);" + newline
                + "}",
                "FileStream* File::OpenRead(const char* filePath)" + newline
                + "{" + newline
                + "\tif (helengine::gamecube::GameCubeDiscFileSystem::CanHandlePath(filePath)) {" + newline
                + "\t\treturn helengine::gamecube::GameCubeDiscFileSystem::OpenRead(filePath);" + newline
                + "\t}" + newline + newline
                + "\treturn new FileStream(filePath, FileMode::Open, FileAccess::Read, FileShare::Read);" + newline
                + "}",
                StringComparison.Ordinal);

            if (!string.Equals(contents, updatedContents, StringComparison.Ordinal)) {
                File.WriteAllText(filePath, updatedContents);
            }
        }
    }
}
