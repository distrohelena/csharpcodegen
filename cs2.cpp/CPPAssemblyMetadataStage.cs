using cs2.core.Pipeline;
using System.Xml.Linq;

namespace cs2.cpp {
    /// <summary>
    /// Extracts assembly metadata from the source project file and applies it to the C++ conversion report.
    /// </summary>
    internal sealed class CPPAssemblyMetadataStage : IConversionStage {
        /// <summary>
        /// Holds the converter that receives the resolved assembly metadata values.
        /// </summary>
        readonly CPPCodeConverter Owner;

        /// <summary>
        /// Initializes the metadata stage for the owning converter.
        /// </summary>
        /// <param name="owner">Converter that receives resolved assembly metadata.</param>
        public CPPAssemblyMetadataStage(CPPCodeConverter owner) {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>
        /// Reads assembly metadata from the active project file and stores the normalized values on the converter.
        /// </summary>
        /// <param name="session">The active conversion session.</param>
        public void Execute(ConversionSession session) {
            string assembly = session.Project.AssemblyName ?? string.Empty;
            string version = string.Empty;
            string framework = string.Empty;

            string projectFile = session.Project.FilePath;
            if (!string.IsNullOrEmpty(projectFile) && File.Exists(projectFile)) {
                try {
                    XDocument document = XDocument.Load(projectFile);
                    string assemblyProperty = ReadProperty(document, "AssemblyName");
                    if (!string.IsNullOrEmpty(assemblyProperty)) {
                        assembly = assemblyProperty;
                    }

                    string versionProperty = ReadProperty(document, "Version");
                    if (string.IsNullOrEmpty(versionProperty)) {
                        versionProperty = ReadProperty(document, "AssemblyVersion");
                    }
                    if (string.IsNullOrEmpty(versionProperty)) {
                        versionProperty = ReadProperty(document, "FileVersion");
                    }
                    if (!string.IsNullOrEmpty(versionProperty)) {
                        version = versionProperty;
                    }

                    string frameworkProperty = ReadProperty(document, "TargetFramework");
                    if (string.IsNullOrEmpty(frameworkProperty)) {
                        frameworkProperty = ReadProperty(document, "TargetFrameworks");
                    }
                    if (!string.IsNullOrEmpty(frameworkProperty)) {
                        framework = frameworkProperty;
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"Warning: unable to read C++ assembly metadata: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(framework) && framework.Contains(';')) {
                string[] frameworks = framework.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                string firstFramework = frameworks.FirstOrDefault();
                if (!string.IsNullOrEmpty(firstFramework)) {
                    framework = firstFramework;
                }
            }

            Owner.SetAssemblyMetadata(assembly, version, framework);
        }

        /// <summary>
        /// Reads a project property by local XML name.
        /// </summary>
        /// <param name="document">The parsed project document.</param>
        /// <param name="propertyName">The property element name to search for.</param>
        /// <returns>The resolved property value when present; otherwise null.</returns>
        static string ReadProperty(XDocument document, string propertyName) {
            if (document.Root == null) {
                return null;
            }

            XElement property = document.Root
                .Descendants()
                .FirstOrDefault(node => node.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (property == null) {
                return null;
            }

            return property.Value;
        }
    }
}
