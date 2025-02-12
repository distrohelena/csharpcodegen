using cs2.core;

namespace cs2.ts {
    public class TypeScriptLayerContext : LayerContext {
        public TypeScriptLayerContext(TypeScriptProgram program)
            : base(program) {
        }

        public override void AddType(ConvertedVariableType varType) {
            ConvertedClass? cl = Program.Classes.Find(c => c.Name == varType.GetTypeScriptType((TypeScriptProgram)Program));
            AddClass(cl);
        }
    }
}
