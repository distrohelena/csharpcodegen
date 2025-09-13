using cs2.core.symbols;
using Newtonsoft.Json;
using PATH = System.IO.Path;

namespace cs2.ts {
    /// <summary>
    /// Represents a known TS class/interface/enum available in the runtime, loaded from extracted symbol JSON.
    /// </summary>
    public class TypeScriptKnownClass {
        public string Name { get; set; }
        public string Path { get; set; }
        public string? Replacement { get; set; }
        public bool GenericVoid { get; set; }
        public List<Symbol> Symbols { get; private set; }
        public bool IsType { get; set; }

        /// <summary>
        /// Creates a known class referencing a TS module path. Loads its symbol metadata from .net.ts JSON.
        /// </summary>
        public TypeScriptKnownClass(string name,
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

            string jsonPath = PATH.Combine(".net.ts", Path + ".json");
            string jsonData = File.ReadAllText(jsonPath);
            Symbols = JsonConvert.DeserializeObject<List<Symbol>>(jsonData);
        }

        public override string ToString() {
            return $"{Name} - {Path}";
        }
    }
}
