namespace cs2.core.symbols {
    public class ClassMember {
        public string Type { get; set; } // "method" or "property"
        public string Name { get; set; } // Name of the method or property
        public string Value { get; set; } // Name of the method or property
        public List<Parameter> Parameters { get; set; } // For methods, contains parameters
        public string ReturnType { get; set; } // For methods, contains the return type
        public string PropertyType { get; set; } // For properties, contains the type

        public override string ToString() {
            return $"{Type} {Name}";
        }
    }
}
