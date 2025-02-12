using cs2.core.json;
using Newtonsoft.Json;
using PATH = System.IO.Path;

namespace cs2.ts {
    public class TypeScriptKnownClass {
        public string Name { get; set; }
        public string Path { get; set; }
        public string? Replacement { get; set; }
        public bool GenericVoid { get; set; }
        public List<Symbol> Symbols { get; private set; }
        public bool IsType { get; set; }

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
