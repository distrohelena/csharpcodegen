namespace cs2.core {
    public class GenericKnownClass : KnownClass {
        public int Start { get; set; }
        public int TotalImports { get; set; }
        public bool VoidReturn { get; set; }

        public GenericKnownClass(int total, int start, bool voidReturn, string name, string path, string replacement = "")
            : base(name, path, replacement, false, true) {
            TotalImports = total;
            Start = start;
            VoidReturn = voidReturn;
        }
    }
}
