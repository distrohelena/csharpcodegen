#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Compares the browser JSON integer and display APIs with actual .NET values and failures.</summary>
    public sealed class TypeScriptJsonNumericParityTests {
        /// <summary>Preserves lexical number validation, bounds, and kind-sensitive display in the emitted runtime.</summary>
        [Fact]
        public void JsonNumericRuntimeMatchesDotNet() {
            string[] inputs = { "null", "true", "false", "\"a\\nb\"", "0", "-0", "1.0", "1e0", "1e+03", "-2147483648", "2147483647", "2147483648", "-2147483649", "9007199254740991", "9223372036854775807", "-9223372036854775808", "9223372036854775808", "-9223372036854775809", "{ \"x\" : 1.0 }", "[ 1, 2 ]" };
            List<object> rows = new List<object>();
            foreach (string input in inputs) {
                using JsonDocument document = JsonDocument.Parse(input);
                JsonElement element = document.RootElement;
                bool? int32Ok = null;
                bool? int64Ok = null;
                int int32 = 0;
                long int64 = 0;
                if (element.ValueKind == JsonValueKind.Number) {
                    int32Ok = element.TryGetInt32(out int32);
                    int64Ok = element.TryGetInt64(out int64);
                }
                rows.Add(new { Input = input, Display = element.ToString(), Raw = element.GetRawText(), Int32Ok = int32Ok, Int32 = int32, Int64Ok = int64Ok, Int64 = int64.ToString(CultureInfo.InvariantCulture) });
            }
            string fixture = Path.GetTempFileName();
            try {
                File.WriteAllText(fixture, JsonSerializer.Serialize(rows));
                ProcessStartInfo start = new ProcessStartInfo("node") {
                    WorkingDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                start.Environment["SSN_JSON_PARITY_FIXTURE"] = fixture;
                start.ArgumentList.Add("--test");
                start.ArgumentList.Add("cs2.ts.tests/tools/json-numeric-encoding-parity.test.cjs");
                using Process process = Process.Start(start);
                if (process == null) throw new InvalidOperationException("Could not start JSON parity verification.");
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, output + error);
            } finally {
                File.Delete(fixture);
            }
        }
    }
}
