namespace cs2.core.symbols {
    public class Parameter {
        public string Name { get; set; } // Parameter name
        public string Type { get; set; } // Parameter type

        public override string ToString() {
            return $"{Type} {Name}";
        }
    }
}
