namespace cs2.core {
    public abstract class LayerContext {
        public ConvertedProgram Program { get; private set; }
        public List<ConvertedClass?> Class { get; private set; }
        public List<ConvertedVariable?> Var { get; private set; }
        public List<FunctionStack?> Function { get; private set; }

        public int DepthClass { get { return Class.Count; } }
        public int DepthFunction { get { return Function.Count; } }

        public bool IdentifyingFunction { get; private set; }

        public LayerContext(ConvertedProgram program) {
            Program = program;
            Class = new List<ConvertedClass?>();
            Var = new List<ConvertedVariable?>();
            Function = new List<FunctionStack?>();
        }

        public void AddFunction(FunctionStack? fn) {
            Function.Add(fn);
        }

        public abstract void AddType(ConvertedVariableType? varType);
           

        public void AddClass(ConvertedClass? cl) {
            if (cl == null) {
                //Debugger.Break();
            }
            Class.Add(cl);
        }

        public ConvertedClass? GetCurrentClass() {
            if (Class.Count == 0) {
                return null;
            }
            return Class[Class.Count - 1];
        }

        public int GetClassLayer() {
            return Class.Count;
        }

        public FunctionStack? GetCurrentFunction() {
            if (Function.Count == 0) {
                return null;
            }
            return Function[Function.Count - 1];
        }

        public void PopClass(int startDepth) {
            int total = Class.Count - startDepth;
            for (int i = 0; i < total; i++) {
                Class.RemoveAt(Class.Count - 1);
            }
        }

        public List<ConvertedClass> SavePopClass(int startDepth) {
            List<ConvertedClass> list = new List<ConvertedClass>();
            int total = Class.Count - startDepth;
            if (total > 1) {
                //Debugger.Break();
            }

            for (int i = 0; i < total; i++) {
                list.Add(Class[Class.Count - i - 1]);
            }

            PopClass(startDepth);

            return list;
        }

        public void LoadClass(List<ConvertedClass> list) {
            Class.AddRange(list);
        }

        public void PopFunction(int startDepth) {
            int total = Function.Count - startDepth;
            for (int i = 0; i < total; i++) {
                Function.RemoveAt(Function.Count - 1);
            }
        }
    }
}
