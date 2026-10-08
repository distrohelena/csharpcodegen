#nullable disable
using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Exercises portable timer and weak transaction buffer behavior through the real JavaScript runtime.</summary>
    public sealed class TypeScriptSchedulingAndWeakTableRuntimeTests {
        /// <summary>Ensures maintained runtime templates are shipped and usable by translated applications.</summary>
        [Theory]
        [InlineData("timer-runtime-parity.test.cjs")]
        [InlineData("bit-operations-runtime.test.cjs")]
        [InlineData("pbkdf2-static-runtime.test.cjs")]
        [InlineData("cryptographic-operations-runtime.test.cjs")]
        [InlineData("json-serializer-utf8-runtime.test.cjs")]
        [InlineData("iterable-collection-contracts.test.cjs")]
        [InlineData("conditional-weak-table-runtime.test.cjs")]
        public void RuntimeBehaviorMatchesManagedContracts(string script) {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            ProcessStartInfo start = new ProcessStartInfo("node") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("--expose-gc");
            start.ArgumentList.Add("--test");
            start.ArgumentList.Add(Path.Combine("cs2.ts.tests", "tools", script));
            using Process process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Could not start runtime contract verification.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, output + error);
        }

        /// <summary>Ensures newly maintained framework shims are registered and copied beside the converter assembly.</summary>
        /// <param name="name">Runtime requirement identifier used by generated imports.</param>
        /// <param name="modulePath">Runtime module path relative to the maintained template root.</param>
        [Theory]
        [InlineData("Timeout", "system/threading/timeout.ts")]
        [InlineData("BitOperations", "system/numerics/bit-operations.ts")]
        [InlineData("CryptographicOperations", "system/security/cryptography/cryptographic-operations.ts")]
        public void RuntimeRequirementIsRegisteredAndCopied(string name, string modulePath) {
            TypeScriptRuntimeRequirementDefinition requirement = Assert.Single(
                TypeScriptRuntimeRequirementCatalog.BaseRequirements.Where(candidate => candidate.Name == name));
            Assert.Equal("./" + Path.ChangeExtension(modulePath.Replace('\\', '/'), null), requirement.Path);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, ".net.ts", modulePath)),
                $"The runtime template '{modulePath}' was not copied to the converter output.");
        }
    }
}
