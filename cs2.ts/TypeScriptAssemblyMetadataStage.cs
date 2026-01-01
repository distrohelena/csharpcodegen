using cs2.core.Pipeline;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace cs2.ts {
    /// <summary>
    /// Conversion stage that extracts assembly metadata from the source project file.
    /// </summary>
    internal sealed class TypeScriptAssemblyMetadataStage : IConversionStage {
        /// <summary>
        /// Holds the converter that receives resolved assembly metadata values.
        /// </summary>
        readonly TypeScriptCodeConverter owner;

        /// <summary>
        /// Initializes the stage with the converter to update.
        /// </summary>
        /// <param name="owner">The converter that receives metadata values.</param>
        public TypeScriptAssemblyMetadataStage(TypeScriptCodeConverter owner) {
            this.owner = owner;
        }

        /// <summary>
        /// Reads assembly metadata from the project file and applies it to the converter.
        /// </summary>
        /// <param name="session">The conversion session being processed.</param>
        public void Execute(ConversionSession session) {
            string assembly = session.Project.AssemblyName;
            if (string.IsNullOrEmpty(assembly)) {
                assembly = string.Empty;
            }
            string version = string.Empty;
            string framework = string.Empty;

            string projectFile = session.Project.FilePath;
            if (!string.IsNullOrEmpty(projectFile) && File.Exists(projectFile)) {
                try {
                    XDocument document = XDocument.Load(projectFile);
                    string assemblyProp = ReadProperty(document, "AssemblyName");
                    if (!string.IsNullOrEmpty(assemblyProp)) {
                        assembly = assemblyProp;
                    }

                    string versionProp = ReadProperty(document, "Version");
                    if (string.IsNullOrEmpty(versionProp)) {
                        versionProp = ReadProperty(document, "AssemblyVersion");
                    }
                    if (string.IsNullOrEmpty(versionProp)) {
                        versionProp = ReadProperty(document, "FileVersion");
                    }
                    if (!string.IsNullOrEmpty(versionProp)) {
                        version = versionProp;
                    }

                    string frameworkProp = ReadProperty(document, "TargetFramework");
                    if (string.IsNullOrEmpty(frameworkProp)) {
                        frameworkProp = ReadProperty(document, "TargetFrameworks");
                    }
                    if (!string.IsNullOrEmpty(frameworkProp)) {
                        framework = frameworkProp;
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"Warning: unable to read assembly metadata: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(framework) && framework.Contains(';')) {
                string[] frameworks = framework.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                string firstFramework = frameworks.FirstOrDefault();
                if (!string.IsNullOrEmpty(firstFramework)) {
                    framework = firstFramework;
                }
            }

            owner.SetAssemblyMetadata(assembly, version, framework);
        }

        /// <summary>
        /// Gets the value of a project property by name, if present.
        /// </summary>
        /// <param name="document">The project file XML document.</param>
        /// <param name="propertyName">The property name to look up.</param>
        /// <returns>The property value if found; otherwise null.</returns>
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
