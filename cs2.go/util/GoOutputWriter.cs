using System;
using System.Collections.Generic;
using System.IO;

namespace cs2.go.util {
    /// <summary>
    /// Wraps a text writer with helpers for Go emission.
    /// </summary>
    public class GoOutputWriter {
        /// <summary>
        /// Initializes a new output writer.
        /// </summary>
        /// <param name="writer">The underlying writer.</param>
        public GoOutputWriter(TextWriter writer) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }

            Writer = writer;
        }

        /// <summary>
        /// Gets the underlying text writer.
        /// </summary>
        public TextWriter Writer { get; }

        /// <summary>
        /// Gets the indent string used when writing indented output.
        /// </summary>
        public string IndentString { get; } = "\t";

        /// <summary>
        /// Gets the current indent level.
        /// </summary>
        public int IndentLevel { get; private set; }

        /// <summary>
        /// Increases the indent level by one.
        /// </summary>
        public void Indent() {
            IndentLevel++;
        }

        /// <summary>
        /// Decreases the indent level by one.
        /// </summary>
        public void Outdent() {
            if (IndentLevel > 0) {
                IndentLevel--;
            }
        }

        /// <summary>
        /// Writes raw text without indentation.
        /// </summary>
        /// <param name="value">The text to write.</param>
        public void Write(string value) {
            Writer.Write(value);
        }

        /// <summary>
        /// Writes a blank line.
        /// </summary>
        public void WriteLine() {
            Writer.WriteLine();
        }

        /// <summary>
        /// Writes a line without indentation.
        /// </summary>
        /// <param name="value">The line to write.</param>
        public void WriteLine(string value) {
            Writer.WriteLine(value);
        }

        /// <summary>
        /// Writes text with the current indentation.
        /// </summary>
        /// <param name="value">The text to write.</param>
        public void WriteIndented(string value) {
            WriteIndent();
            Writer.Write(value);
        }

        /// <summary>
        /// Writes a line with the current indentation.
        /// </summary>
        /// <param name="value">The line to write.</param>
        public void WriteIndentedLine(string value) {
            WriteIndent();
            Writer.WriteLine(value);
        }

        /// <summary>
        /// Writes tokenized lines with continuation indentation after newlines.
        /// </summary>
        /// <param name="lines">The token list to write.</param>
        /// <param name="continuationIndent">The indent to apply after line breaks.</param>
        public void WriteLines(List<string> lines, string continuationIndent) {
            if (lines == null || lines.Count == 0) {
                return;
            }

            if (continuationIndent == null) {
                continuationIndent = string.Empty;
            }

            for (int i = 0; i < lines.Count; i++) {
                string value = lines[i];
                Writer.Write(value);
                if (value.IndexOf("\n", StringComparison.Ordinal) != -1 && i != lines.Count - 1) {
                    Writer.Write(continuationIndent);
                }
            }
        }

        /// <summary>
        /// Writes the current indentation to the underlying writer.
        /// </summary>
        void WriteIndent() {
            for (int i = 0; i < IndentLevel; i++) {
                Writer.Write(IndentString);
            }
        }
    }
}
