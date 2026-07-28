using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Emits one direct Tracy scope at the beginning of a generated C++ function body and records its source identity.
    /// </summary>
    public class CPPGeneratedFunctionProfilingScopeEmitter {
        readonly CPPGeneratedFunctionProfilingManifest Manifest;

        /// <summary>
        /// Initializes a scope emitter that records every scope it writes.
        /// </summary>
        /// <param name="manifest">Manifest collector for the active conversion run.</param>
        public CPPGeneratedFunctionProfilingScopeEmitter(CPPGeneratedFunctionProfilingManifest manifest) {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        }

        /// <summary>
        /// Writes a static Tracy source location and matching scoped zone for one generated body.
        /// </summary>
        /// <param name="sourceWriter">Generated C++ source writer.</param>
        /// <param name="generatedFilePath">C++ source file receiving the scope.</param>
        /// <param name="sourceLocation">Maintained source identity for the generated member.</param>
        public void Write(TextWriter sourceWriter, string generatedFilePath, ConversionSourceLocation sourceLocation) {
            if (sourceWriter == null) {
                throw new ArgumentNullException(nameof(sourceWriter));
            }

            if (sourceLocation == null) {
                return;
            }

            sourceWriter.WriteLine($"    static const tracy::SourceLocationData he_generated_profiler_location{{ \"{Escape(sourceLocation.MaintainedSymbol)}\", \"{Escape(sourceLocation.MaintainedSymbol)}\", \"{Escape(sourceLocation.FilePath)}\", {sourceLocation.LineNumber}, 0 }};");
            sourceWriter.WriteLine("    tracy::ScopedZone he_generated_profiler_zone(&he_generated_profiler_location, true);");
            Manifest.Add(generatedFilePath, sourceLocation);
        }

        /// <summary>
        /// Escapes a maintained source value for a C++ string literal.
        /// </summary>
        /// <param name="value">Raw source value.</param>
        /// <returns>C++ string literal content.</returns>
        static string Escape(string value) {
            return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        }
    }
}
