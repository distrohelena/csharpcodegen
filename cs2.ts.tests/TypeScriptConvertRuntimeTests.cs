using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies that copied browser conversion primitives preserve the supported .NET Convert contracts.</summary>
    public sealed class TypeScriptConvertRuntimeTests {
        /// <summary>Executes primitive string and Int64 conversion parity checks against maintained runtime source.</summary>
        [Fact]
        public void SourceRuntimePreservesConvertContracts() {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var start = new ProcessStartInfo("node") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("--test");
            start.ArgumentList.Add("cs2.ts.tests/tools/convert-runtime-parity.test.cjs");
            var process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Could not start Convert runtime verification.");
            using var ownedProcess = process;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error + output);
        }
    }
}
