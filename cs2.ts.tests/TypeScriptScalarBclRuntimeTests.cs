using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies scalar browser BCL additions against maintained TypeScript runtime source.</summary>
    public sealed class TypeScriptScalarBclRuntimeTests {
        /// <summary>Executes scalar BCL parity checks with the same Node runtime used by generated browser modules.</summary>
        [Fact]
        public void SourceRuntimePreservesScalarBclContracts() {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var start = new ProcessStartInfo("node") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("--test");
            start.ArgumentList.Add("cs2.ts.tests/tools/scalar-bcl-runtime-parity.test.cjs");
            var process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Could not start scalar BCL runtime verification.");
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
