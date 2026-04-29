namespace cs2.cpp {
    /// <summary>
    /// Identifies the compiler family the generated C++ output targets.
    /// </summary>
    public enum CPPCompilerKind {
        /// <summary>
        /// Microsoft Visual C++.
        /// </summary>
        Msvc,

        /// <summary>
        /// GNU Compiler Collection C++.
        /// </summary>
        Gcc,

        /// <summary>
        /// LLVM Clang C++.
        /// </summary>
        Clang
    }
}
