namespace cs2.core.symbols {
    public class Symbol {
        public string Type { get; set; } // "class", "function", or "variable"
        public string Name { get; set; } // Name of the class, function, or variable
        public List<ClassMember> Members { get; set; } // For classes, contains members
        public List<Parameter> Parameters { get; set; } // For functions, contains parameters
        public string ReturnType { get; set; } // For functions, contains the return type
        public string VariableType { get; set; } // For variables, contains the type

        public Symbol() {
            Members = new List<ClassMember>();
            Parameters = new List<Parameter>();
        }

        public override string ToString() {
            return $"{Type} {Name}";
        }
    }
}
