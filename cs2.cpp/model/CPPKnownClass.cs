using cs2.core.symbols;
using Newtonsoft.Json;
using PATH = System.IO.Path;

namespace cs2.cpp {
    public class CPPKnownClass {
        public string Name { get; set; }
        public string Path { get; set; }
        public string? Replacement { get; set; }
        public bool GenericVoid { get; set; }
        public Symbol Symbol { get; set; }
        public bool IsType { get; set; }

        public CPPKnownClass(string name,
            string path,
            string replacement = "",
            bool genericVoid = false,
            bool isType = false
            ) {
            Name = name;
            Path = path;
            Replacement = replacement;
            GenericVoid = genericVoid;
            IsType = isType;
        }

        public override string ToString() {
            return $"{Name} - {Path}";
        }
    }
}
