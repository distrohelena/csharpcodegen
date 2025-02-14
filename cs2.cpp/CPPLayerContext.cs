using cs2.core;

namespace cs2.cpp {
    public class CPPLayerContext : LayerContext {
        public CPPLayerContext(ConversionProgram program)
            : base(program) {
        }

        public override void AddType(VariableType? varType) {
            ConversionClass? cl = Program.Classes.Find(c => c.Name == varType.GetTypeScriptType(Program));
            AddClass(cl);
        }
    }
}
