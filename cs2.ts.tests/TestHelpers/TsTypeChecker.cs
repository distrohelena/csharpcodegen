using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace cs2.ts.tests.TestHelpers {
    internal static class TsTypeChecker {
        private static bool _installed;
        private static bool _tsAvailable = true;

        private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        private static string TsRuntimeRoot => Path.Combine(RepoRoot, "cs2.ts", ".net.ts");
        private static string ToolsDir => Path.Combine(RepoRoot, "cs2.ts.tests", "tools");

        private static void EnsureTypeScriptInstalled() {
            if (_installed) return;

            // Verify node and npm are available
            try {
                Run("node", "-v", RepoRoot);
                Run("npm", "-v", TsRuntimeRoot);
            } catch (Exception) {
                _tsAvailable = false;
                _installed = true;
                return;
            }

            // Prefer deterministic install if lockfile exists
            var lockFile = Path.Combine(TsRuntimeRoot, "package-lock.json");
            var args = File.Exists(lockFile) ? "ci --silent" : "install --silent";
            Run("npm", args, TsRuntimeRoot);

            _installed = true;
        }

        private static string BuildPrelude(bool asyncNeeded) {
            var sb = new StringBuilder();
            sb.AppendLine("// minimal ambient types for converter output");
            sb.AppendLine("type Int16 = number; type UInt16 = number; type Int32 = number; type UInt32 = number; type Single = number; type Int64 = number; type UInt64 = number; type Boolean = boolean;");
            sb.AppendLine("const Int32: number = 0;");
            sb.Append("class __C { ");
            if (asyncNeeded) sb.Append("async ");
            sb.AppendLine("m(): any {");
            return sb.ToString();
        }

        private static string BuildWrapper(string body) {
            var asyncNeeded = body.Contains("await ");
            var prelude = BuildPrelude(asyncNeeded);
            var suffix = "}\n}\n";
            var code = prelude + body + (body.TrimEnd().EndsWith(";") || body.TrimEnd().EndsWith("}") ? "" : ";\n") + suffix;
            return code;
        }

        public static void AssertValidTypeScript(System.Collections.Generic.IEnumerable<string> lines) {
            EnsureTypeScriptInstalled();

            var code = string.Concat(lines);
            var wrapped = BuildWrapper(code);
            var temp = Path.Combine(Path.GetTempPath(), "tscheck_" + Guid.NewGuid().ToString("N") + ".ts");
            File.WriteAllText(temp, wrapped);

            if (!_tsAvailable) return;

            var psi = new ProcessStartInfo {
                FileName = "node",
                Arguments = $"\"{Path.Combine(ToolsDir, "tscheck.js")}\" \"{TsRuntimeRoot}\" \"{temp}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            try {
                Assert.True(p.ExitCode == 0, $"TypeScript failed to compile.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}\nCODE:\n{wrapped}");
            } finally {
                try { File.Delete(temp); } catch { }
            }
        }

        private static void Run(string fileName, string arguments, string workingDirectory) {
            var psi = new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) {
                throw new InvalidOperationException($"Command failed: {fileName} {arguments}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            }
        }
    }
}
