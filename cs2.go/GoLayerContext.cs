using cs2.core;

namespace cs2.go {
    /// <summary>
    /// LayerContext specialization for Go conversion, tracking class scope for type inference.
    /// </summary>
    public class GoLayerContext : LayerContext {
        /// <summary>
        /// Initializes a new Go layer context for the given program.
        /// </summary>
        /// <param name="program">The Go program that owns the conversion state.</param>
        public GoLayerContext(GoProgram program)
            : base(program) {
        }

        /// <summary>
        /// Adds a type to the current context by resolving the Go type name in the program's class list.
        /// </summary>
        /// <param name="varType">The variable type to resolve and push onto the context.</param>
        public override void AddType(VariableType varType) {
            GoProgram goProgram = (GoProgram)Program;
            string typeName = varType.GetGoTypeName(goProgram);
            ConversionClass cl = goProgram.GetClassByName(typeName);
            AddClass(cl);
        }
    }
}
