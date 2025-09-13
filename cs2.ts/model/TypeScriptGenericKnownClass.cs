namespace cs2.ts {
    /// <summary>
    /// Known class that requires importing multiple generic arities (e.g., Action0..Action16).
    /// </summary>
    public class TypeScriptGenericKnownClass : TypeScriptKnownClass {
        public int Start { get; set; }
        public int TotalImports { get; set; }
        public bool VoidReturn { get; set; }

        /// <summary>
        /// total: max imports, start: first arity with suffix, voidReturn: append arity for void returns.
        /// </summary>
        public TypeScriptGenericKnownClass(int total, int start, bool voidReturn, string name, string path, string replacement = "")
            : base(name, path, replacement, false, true) {
            TotalImports = total;
            Start = start;
            VoidReturn = voidReturn;
        }
    }
}
