using cs2.core.symbols;
using Newtonsoft.Json;
using PATH = System.IO.Path;

namespace cs2.ts {
    /// <summary>
    /// Represents a known TS class/interface/enum available in the runtime, loaded from extracted symbol JSON.
    /// </summary>
    public class TypeScriptKnownClass {
        /// <summary>
        /// Creates a known class referencing a TS module path. Loads its symbol metadata from .net.ts JSON.
        /// </summary>
        /// <param name="name">The C# type name to map.</param>
        /// <param name="path">The module path that provides the runtime symbol.</param>
        /// <param name="replacement">Optional replacement import identifier.</param>
        /// <param name="genericVoid">Whether generic arguments default to void when missing.</param>
        /// <param name="isType">Whether the import is type-only.</param>
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

        /// <summary>
        /// Gets or sets the C# type name mapped to the runtime symbol.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Gets or sets the module path that provides the runtime symbol.
        /// </summary>
        public string Path { get; set; }
        /// <summary>
        /// Gets or sets the replacement import identifier, if a rename is required.
        /// </summary>
        public string Replacement { get; set; }
        /// <summary>
        /// Gets or sets whether generic arguments default to void when unspecified.
        /// </summary>
        public bool GenericVoid { get; set; }
        /// <summary>
        /// Gets the extracted symbol metadata used for member remapping.
        /// </summary>
        public List<Symbol> Symbols { get; private set; }
        /// <summary>
        /// Gets or sets whether the import is type-only.
        /// </summary>
        public bool IsType { get; set; }

        /// <summary>
        /// Returns a readable label for debugging and logs.
        /// </summary>
        /// <returns>The display label for the known class.</returns>
        public override string ToString() {
            return $"{Name} - {Path}";
        }
    }
}
