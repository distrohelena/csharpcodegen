using cs2.core;

namespace cs2.cpp {
    public class CPPLayerContext : LayerContext {
        public CPPLayerContext(ConvertedProgram program)
            : base(program) {
        }

        public override void AddType(ConvertedVariableType? varType) {
            ConvertedClass? cl = Program.Classes.Find(c => c.Name == varType.GetTypeScriptType(Program));
            AddClass(cl);
        }
    }
}
