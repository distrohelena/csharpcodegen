using cs2.core;

namespace cs2.ts {
    /// <summary>
    /// LayerContext specialization for TypeScript conversion, tracking class scope for type inference.
    /// </summary>
    public class TypeScriptLayerContext : LayerContext {
        public TypeScriptLayerContext(TypeScriptProgram program)
            : base(program) {
        }

        /// <summary>
        /// Adds a type to the current context by resolving the TS type name in the program's class list.
        /// </summary>
        public override void AddType(VariableType varType) {
            ConversionClass? cl = Program.Classes.Find(c => c.Name == varType.GetTypeScriptType((TypeScriptProgram)Program));
            AddClass(cl);
        }
    }
}
