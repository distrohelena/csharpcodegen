namespace cs2.ts.util {
    /// <summary>
    /// Describes one language-server text edit using zero-based LSP line and UTF-16 character positions.
    /// </summary>
    public readonly struct TypeScriptTextEdit {
        /// <summary>
        /// Initializes a new text edit.
        /// </summary>
        /// <param name="startLine">The zero-based line where the replaced range starts.</param>
        /// <param name="startCharacter">The UTF-16 offset within the start line.</param>
        /// <param name="endLine">The zero-based line where the replaced range ends.</param>
        /// <param name="endCharacter">The UTF-16 offset within the end line.</param>
        /// <param name="newText">The replacement text.</param>
        public TypeScriptTextEdit(int startLine, int startCharacter, int endLine, int endCharacter, string newText) {
            StartLine = startLine;
            StartCharacter = startCharacter;
            EndLine = endLine;
            EndCharacter = endCharacter;
            NewText = newText ?? string.Empty;
        }

        /// <summary>
        /// Gets the zero-based line where the replaced range starts.
        /// </summary>
        public int StartLine { get; }

        /// <summary>
        /// Gets the UTF-16 offset within the start line.
        /// </summary>
        public int StartCharacter { get; }

        /// <summary>
        /// Gets the zero-based line where the replaced range ends.
        /// </summary>
        public int EndLine { get; }

        /// <summary>
        /// Gets the UTF-16 offset within the end line.
        /// </summary>
        public int EndCharacter { get; }

        /// <summary>
        /// Gets the replacement text.
        /// </summary>
        public string NewText { get; }
    }
}
