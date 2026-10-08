#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Checks browser HTML/database-null semantics against the real .NET implementation.</summary>
    public sealed class TypeScriptWebUtilityRuntimeTests {
        /// <summary>Ensures generated references resolve to maintained runtime modules instead of missing globals.</summary>
        [Fact]
        public void RuntimeCatalogRegistersHtmlAndDatabaseNull() {
            Assert.Contains(TypeScriptRuntimeRequirementCatalog.BaseRequirements, row => row.Name == "WebUtility" && row.Path == "./system/net/web-utility");
            Assert.Contains(TypeScriptRuntimeRequirementCatalog.BaseRequirements, row => row.Name == "DBNull" && row.Path == "./system/dbnull");
        }

        /// <summary>Compares every BMP unit plus malformed and valid surrogate sequences with .NET HTML encoding.</summary>
        [Fact]
        public void RuntimeMatchesDotNetHtmlEncodingAndDatabaseNullIdentity() {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string fixture = Path.GetTempFileName();
            try {
                string[] inputs = { null, "", "<a title=\"x&y\">'é'</a>", "&amp;", new string(Enumerable.Range(0, 65536).Select(value => (char)value).ToArray()), "\ud800", "\udfff", "\ud800A\udfff", "\ud83d\ude00", "\udbff\udfff", "\ud800\ud800\udc00" };
                object[] rows = inputs.Select((input, index) => (object)new {
                    Name = "case-" + index,
                    Units = input == null ? null : input.Select(character => (int)character).ToArray(),
                    Expected = WebUtility.HtmlEncode(input)
                }).ToArray();
                File.WriteAllText(fixture, JsonSerializer.Serialize(rows));
                ProcessStartInfo start = new ProcessStartInfo("node") {
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                start.Environment["SSN_HTML_PARITY_FIXTURE"] = fixture;
                start.ArgumentList.Add("--test");
                start.ArgumentList.Add("cs2.ts.tests/tools/web-utility-dbnull-runtime.test.cjs");
                using Process process = Process.Start(start);
                if (process == null) throw new InvalidOperationException("Could not start runtime parity verification.");
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, error + output);
            } finally {
                File.Delete(fixture);
            }
        }
    }
}