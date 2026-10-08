using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Checks collection materialization used by converted sealed-entry payloads.</summary>
    public sealed class TypeScriptHashSetMaterializationTests {
        /// <summary>Executes snapshot, equality and numeric-item regressions against maintained runtime source.</summary>
        [Fact]
        public void SourceRuntimeMaterializesIndependentLists() {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var start = new ProcessStartInfo("node") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("--test");
            start.ArgumentList.Add("cs2.ts.tests/tools/hash-set-materialization.test.cjs");
            var process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Could not start collection runtime verification.");
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
