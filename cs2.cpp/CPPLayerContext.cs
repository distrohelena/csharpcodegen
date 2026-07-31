using cs2.core;

namespace cs2.cpp {
    /// <summary>
    /// Carries C++ backend scope state, including the active managed checked-arithmetic context, through expression lowering.
    /// </summary>
    public class CPPLayerContext : LayerContext {
        /// <summary>
        /// Stores nested checked and unchecked contexts in lexical order so the innermost statement controls arithmetic lowering.
        /// </summary>
        readonly List<bool> CheckedArithmeticContexts;

        /// <summary>
        /// Initializes one C++ lowering context for the supplied conversion program.
        /// </summary>
        /// <param name="program">Conversion program whose class and function scopes are tracked.</param>
        public CPPLayerContext(ConversionProgram program)
            : base(program) {
            CheckedArithmeticContexts = new List<bool>();
        }

        /// <summary>
        /// Gets whether the innermost lexical arithmetic context requires managed overflow checking.
        /// </summary>
        public bool IsCheckedArithmetic => CheckedArithmeticContexts.Count > 0 && CheckedArithmeticContexts[^1];

        /// <summary>
        /// Resolves and pushes a generated class for the supplied variable type.
        /// </summary>
        /// <param name="varType">Variable type whose generated class scope should be entered.</param>
        public override void AddType(VariableType? varType) {
            ConversionClass? cl = Program.Classes.Find(c => c.Name == varType.GetTypeScriptType(Program));
            AddClass(cl);
        }

        /// <summary>
        /// Enters one lexical checked or unchecked arithmetic statement.
        /// </summary>
        /// <param name="isChecked">Whether overflow checking is enabled by the entered statement.</param>
        public void PushCheckedArithmeticContext(bool isChecked) {
            CheckedArithmeticContexts.Add(isChecked);
        }

        /// <summary>
        /// Leaves the innermost lexical checked or unchecked arithmetic statement.
        /// </summary>
        public void PopCheckedArithmeticContext() {
            if (CheckedArithmeticContexts.Count == 0) {
                throw new InvalidOperationException("A checked arithmetic context cannot be removed when none is active.");
            }

            CheckedArithmeticContexts.RemoveAt(CheckedArithmeticContexts.Count - 1);
        }
    }
}
