namespace cs2.core.json {
    public class ClassMember {
        public string Type { get; set; } // "method" or "property"
        public string Name { get; set; } // Name of the method or property
        public List<Parameter> Parameters { get; set; } // For methods, contains parameters
        public string ReturnType { get; set; } // For methods, contains the return type
        public string PropertyType { get; set; } // For properties, contains the type
    }
}
