using System;
using System.Collections.Generic;
using System.IO;
using cs2.ts.util;
using Xunit;

namespace cs2.ts.tests {
    /// <summary>Verifies the TypeScript 7 language-server formatter integration used for generated output.</summary>
    public sealed class TypeScriptCodeFormatterTests {
        /// <summary>Gets the repository's .net.ts runtime folder, whose node_modules hosts the TypeScript 7 package.</summary>
        static string RuntimeRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cs2.ts", ".net.ts"));

        /// <summary>Formats real source through the TypeScript 7 language server with four-space indentation.</summary>
        [Fact]
        public void Format_UsesTypeScript7LanguageServer() {
            List<string> log = new List<string>();
            string formatted = TypeScriptCodeFormatter.Format("class C {\nm( ) {\nreturn   1;\n}\n}\n", RuntimeRoot, log.Add);

            Assert.Equal("class C {\n    m() {\n        return 1;\n    }\n}\n", formatted);
            string executable = TypeScriptCodeFormatter.ResolveLanguageServerExecutable(RuntimeRoot);
            Assert.False(string.IsNullOrEmpty(executable), "The TypeScript 7 native executable must resolve from the runtime node_modules.");
            Assert.Contains(Path.Combine("@typescript", "typescript-"), executable);
        }

        /// <summary>Keeps source untouched apart from the formatter's edits when lines use mixed terminators.</summary>
        [Fact]
        public void Format_PreservesMixedLineTerminators() {
            string source = "const a = 1;\r\nfunction f( ) {\rreturn   1;\n}\r\n";

            string formatted = TypeScriptCodeFormatter.Format(source, RuntimeRoot, null);

            Assert.Equal("const a = 1;\r\nfunction f() {\r    return 1;\n}\r\n", formatted);
        }

        /// <summary>Maps LSP positions with CRLF, lone CR and LF line breaks while treating U+2028 as ordinary text.</summary>
        [Fact]
        public void ApplyEdits_UsesLanguageServerLineBreaks() {
            string source = "a\u2028b\r\nc\rd\ne";
            TypeScriptTextEdit[] edits = new[] {
                new TypeScriptTextEdit(3, 0, 3, 1, "E"),
                new TypeScriptTextEdit(0, 2, 0, 3, "B"),
                new TypeScriptTextEdit(1, 0, 1, 1, "C"),
                new TypeScriptTextEdit(2, 0, 2, 0, "<"),
            };

            string result = TypeScriptLanguageServerFormatter.ApplyEdits(source, edits);

            Assert.Equal("a\u2028B\r\nC\r<d\nE", result);
        }

        /// <summary>Applies an insertion at the very end of the document.</summary>
        [Fact]
        public void ApplyEdits_InsertsAtEndOfDocument() {
            string result = TypeScriptLanguageServerFormatter.ApplyEdits("x\n", new[] { new TypeScriptTextEdit(1, 0, 1, 0, "y") });

            Assert.Equal("x\ny", result);
        }

        /// <summary>Rejects overlapping edits instead of producing corrupted output.</summary>
        [Fact]
        public void ApplyEdits_RejectsOverlappingEdits() {
            TypeScriptTextEdit[] edits = new[] {
                new TypeScriptTextEdit(0, 0, 0, 3, "a"),
                new TypeScriptTextEdit(0, 2, 0, 4, "b"),
            };

            Assert.Throws<InvalidOperationException>(() => TypeScriptLanguageServerFormatter.ApplyEdits("abcdef", edits));
        }

        /// <summary>Fails loudly and logs the server's stderr when the executable is not a working language server.</summary>
        [Fact]
        public void LanguageServerFormat_FailsLoudlyWithStderr() {
            List<string> log = new List<string>();

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                TypeScriptLanguageServerFormatter.Format("node", "const a = 1;\n", Path.GetTempPath(), TimeSpan.FromSeconds(30), log.Add));

            Assert.Contains("TypeScript 7 formatter", failure.Message);
            Assert.Contains(log, line => line.Contains("--lsp", StringComparison.Ordinal));
        }

        /// <summary>Finds the native TypeScript 7 executable in a node_modules layout without running it.</summary>
        [Fact]
        public void ResolveLanguageServerExecutable_FindsPlatformPackage() {
            string root = Path.Combine(Path.GetTempPath(), "cs2.ts.tsgo-resolve." + Guid.NewGuid().ToString("N"));
            try {
                string runtimeDir = Path.Combine(root, ".net.ts");
                string libDir = Path.Combine(runtimeDir, "node_modules", "@typescript", TypeScriptCodeFormatter.GetPlatformPackageName(), "lib");
                Directory.CreateDirectory(libDir);
                string executable = Path.Combine(libDir, TypeScriptCodeFormatter.GetExecutableName());
                File.WriteAllText(executable, string.Empty);

                Assert.Equal(executable, TypeScriptCodeFormatter.ResolveLanguageServerExecutable(runtimeDir));
            } finally {
                Directory.Delete(root, true);
            }
        }
    }
}
