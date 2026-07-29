using System.Text.Json;

namespace cs2.cpp {
    /// <summary>
    /// Writes the generated Tracy support header and the manifest describing scopes emitted by the C++ backend.
    /// </summary>
    public static class CPPGeneratedFunctionProfilingSupportWriter {
        /// <summary>
        /// Writes profiling support files when generated function profiling is enabled.
        /// </summary>
        /// <param name="outputFolder">Root generated output folder.</param>
        /// <param name="enabled">Whether profiling support was enabled for this conversion run.</param>
        /// <param name="manifest">Scopes written by generated source emitters.</param>
        /// <returns>Paths of profiling support files written for the run.</returns>
        public static IReadOnlyList<string> Write(string outputFolder, bool enabled, CPPGeneratedFunctionProfilingManifest manifest) {
            if (string.IsNullOrWhiteSpace(outputFolder)) {
                throw new ArgumentException("Output folder must not be empty.", nameof(outputFolder));
            }

            if (manifest == null) {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (!enabled) {
                return Array.Empty<string>();
            }

            string runtimeFolder = Path.Combine(outputFolder, "runtime");
            Directory.CreateDirectory(runtimeFolder);
            string headerPath = Path.Combine(runtimeFolder, "generated_profiler.hpp");
            string manifestPath = Path.Combine(runtimeFolder, "generated_profiler_manifest.json");
            File.WriteAllText(headerPath, BuildHeaderText());
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { scopes = manifest.Entries.Select(CreateManifestEntry).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
            return new[] { headerPath, manifestPath };
        }

        /// <summary>
        /// Creates one serializable manifest entry from a recorded profiling scope.
        /// </summary>
        /// <param name="scope">Scope emitted by the C++ backend.</param>
        /// <returns>Anonymous manifest entry with maintained source identity.</returns>
        static object CreateManifestEntry(CPPGeneratedFunctionProfilingScope scope) {
            return new {
                generatedFilePath = scope.GeneratedFilePath,
                assemblyName = scope.SourceLocation.AssemblyName,
                maintainedSymbol = scope.SourceLocation.MaintainedSymbol,
                filePath = scope.SourceLocation.FilePath,
                lineNumber = scope.SourceLocation.LineNumber
            };
        }

        /// <summary>
        /// Builds the lightweight generated include that binds source instrumentation to Tracy's direct C++ API.
        /// </summary>
        /// <returns>Generated header text.</returns>
        static string BuildHeaderText() {
            return """
#pragma once

#include "../helcpp_config.hpp"

#if HE_CPP_GENERATED_FUNCTION_PROFILING
#include <tracy/Tracy.hpp>

#define HE_CPP_GENERATED_PROFILE_ALLOCATE(pointer, byteCount, name) TracyAllocN(pointer, byteCount, name)
#define HE_CPP_GENERATED_PROFILE_FREE(pointer, name) TracyFreeN(pointer, name)
#define HE_CPP_GENERATED_PROFILE_LOCKABLE(type, lock, name) tracy::LockableCtx lock { [] () -> const tracy::SourceLocationData* { static constexpr tracy::SourceLocationData sourceLocation { nullptr, name, TracyFile, TracyLine, 0 }; return &sourceLocation; }() }
#define HE_CPP_GENERATED_PROFILE_BEFORE_LOCK(lock, queued) const bool queued = lock.BeforeLock()
#define HE_CPP_GENERATED_PROFILE_AFTER_LOCK(lock, queued) if (queued) { lock.AfterLock(); }
#define HE_CPP_GENERATED_PROFILE_AFTER_UNLOCK(lock) lock.AfterUnlock()
#endif
""";
        }
    }
}
