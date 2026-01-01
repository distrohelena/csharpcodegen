using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace cs2.ts.util {
    /// <summary>
    /// Ensures the TypeScript runtime metadata JSON files are generated for .net.ts.
    /// </summary>
    public static class TypeScriptRuntimeMetadata {
        /// <summary>
        /// Ensures runtime metadata exists, optionally installing dependencies and running the extractor.
        /// </summary>
        /// <param name="request">The metadata extraction request.</param>
        /// <returns>True when metadata was ensured or already available.</returns>
        public static bool EnsureRuntimeMetadata(TypeScriptRuntimeMetadataRequest request) {
            if (request == null) {
                throw new ArgumentNullException(nameof(request));
            }

            string runtimeDir = request.RuntimeDirectory;
            if (string.IsNullOrWhiteSpace(runtimeDir)) {
                return HandleFailure(request, new InvalidOperationException("Runtime directory was not provided."));
            }

            if (!Directory.Exists(runtimeDir)) {
                return HandleFailure(request, new DirectoryNotFoundException($"Expected TypeScript runtime at '{runtimeDir}'."));
            }

            string extractor = Path.Combine(runtimeDir, "extractor.js");
            if (!File.Exists(extractor)) {
                return HandleFailure(request, new FileNotFoundException($"extractor.js not found in '{runtimeDir}'."));
            }

            if (request.EnsureDependencies) {
                string typesDir = Path.Combine(runtimeDir, "node_modules", "typescript");
                if (!Directory.Exists(typesDir)) {
                    string packageJson = Path.Combine(runtimeDir, "package.json");
                    if (!File.Exists(packageJson)) {
                        return HandleFailure(request, new FileNotFoundException($"Expected package.json in '{runtimeDir}'."));
                    }

                    request.Logger?.Invoke("-- npm install");
                    ProcessStartInfo npmInfo = BuildNpmInstallInfo(runtimeDir);
                    if (!RunProcess(npmInfo, request, request.InstallTimeoutMinutes, "npm install")) {
                        return HandleFailure(request, new InvalidOperationException("npm install failed."));
                    }
                }
            }

            request.Logger?.Invoke($"-- Processing folder: {runtimeDir}");
            ProcessStartInfo extractorInfo = BuildExtractorInfo(runtimeDir, extractor);
            if (!RunProcess(extractorInfo, request, 0, "metadata extractor")) {
                return HandleFailure(request, new InvalidOperationException("Metadata extractor failed."));
            }

            return true;
        }

        /// <summary>
        /// Builds the process start info for npm install.
        /// </summary>
        /// <param name="runtimeDir">The runtime directory.</param>
        /// <returns>The configured process start info.</returns>
        static ProcessStartInfo BuildNpmInstallInfo(string runtimeDir) {
            ProcessStartInfo npmInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                npmInfo = new ProcessStartInfo("cmd.exe", "/c npm install");
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                npmInfo = new ProcessStartInfo("npm", "install");
            } else {
                throw new PlatformNotSupportedException("Unsupported operating system.");
            }

            npmInfo.WorkingDirectory = runtimeDir;
            npmInfo.UseShellExecute = false;
            npmInfo.CreateNoWindow = true;
            npmInfo.RedirectStandardOutput = true;
            npmInfo.RedirectStandardError = true;
            return npmInfo;
        }

        /// <summary>
        /// Builds the process start info for the extractor.
        /// </summary>
        /// <param name="runtimeDir">The runtime directory.</param>
        /// <param name="extractorPath">The extractor script path.</param>
        /// <returns>The configured process start info.</returns>
        static ProcessStartInfo BuildExtractorInfo(string runtimeDir, string extractorPath) {
            return new ProcessStartInfo {
                FileName = "node",
                Arguments = $"\"{extractorPath}\" \"{runtimeDir}\"",
                WorkingDirectory = runtimeDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        /// <summary>
        /// Runs a process and handles output and timeouts.
        /// </summary>
        /// <param name="info">The process start info.</param>
        /// <param name="request">The metadata request.</param>
        /// <param name="timeoutMinutes">The timeout in minutes, or 0 for no timeout.</param>
        /// <param name="label">The label for error messages.</param>
        /// <returns>True when the process succeeds.</returns>
        static bool RunProcess(ProcessStartInfo info, TypeScriptRuntimeMetadataRequest request, int timeoutMinutes, string label) {
            using Process process = Process.Start(info);
            if (process == null) {
                return HandleFailure(request, new InvalidOperationException($"{label} failed to start."));
            }

            if (request.ForwardOutput) {
                process.OutputDataReceived += (_, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        request.Logger?.Invoke(e.Data);
                    }
                };
                process.ErrorDataReceived += (_, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        request.Logger?.Invoke(e.Data);
                    }
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            if (timeoutMinutes > 0) {
                int timeoutMs = (int)TimeSpan.FromMinutes(timeoutMinutes).TotalMilliseconds;
                if (!process.WaitForExit(timeoutMs)) {
                    try {
                        process.Kill();
                    } catch {
                    }
                    return HandleFailure(request, new TimeoutException($"{label} timed out after {timeoutMinutes} minutes."));
                }
            } else {
                process.WaitForExit();
            }

            if (!request.ForwardOutput) {
                string output = process.StandardOutput.ReadToEnd();
                string outputError = process.StandardError.ReadToEnd();

                if (!string.IsNullOrWhiteSpace(output)) {
                    request.Logger?.Invoke(output);
                }
                if (!string.IsNullOrWhiteSpace(outputError)) {
                    request.Logger?.Invoke(outputError);
                }
            }

            if (process.ExitCode != 0) {
                return HandleFailure(request, new InvalidOperationException($"{label} exited with code {process.ExitCode}."));
            }

            return true;
        }

        /// <summary>
        /// Handles a failure according to the request's configuration.
        /// </summary>
        /// <param name="request">The metadata request.</param>
        /// <param name="message">The failure message.</param>
        /// <returns>Always false for convenience.</returns>
        static bool HandleFailure(TypeScriptRuntimeMetadataRequest request, Exception exception) {
            if (request.ThrowOnError) {
                throw exception;
            }

            request.Logger?.Invoke($"Warning: {exception.Message}");
            return false;
        }
    }
}
