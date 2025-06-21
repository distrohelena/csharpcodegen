using System.Diagnostics;

namespace cs2.core {
    public abstract class LayerContext {
        public ConversionProgram Program { get; private set; }
        public List<ConversionClass?> Class { get; private set; }
        public List<ConversionVariable?> Var { get; private set; }
        public List<FunctionStack?> Function { get; private set; }

        public int DepthClass { get { return Class.Count; } }
        public int DepthFunction { get { return Function.Count; } }

        public bool IdentifyingFunction { get; private set; }

        public LayerContext(ConversionProgram program) {
            Program = program;
            Class = new List<ConversionClass?>();
            Var = new List<ConversionVariable?>();
            Function = new List<FunctionStack?>();
        }

        public void AddFunction(FunctionStack? fn) {
            Function.Add(fn);
        }

        public abstract void AddType(VariableType? varType);


        public int AddClass(ConversionClass? cl) {
            if (cl == null) {
                //Debugger.Break();
            }

            int startValue = DepthClass;
            Class.Add(cl);

            return startValue;
        }

        public ConversionClass? GetCurrentClass() {
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

        public List<ConversionClass> SavePopClass(int startDepth) {
            List<ConversionClass> list = new List<ConversionClass>();
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

        public void LoadClass(List<ConversionClass> list) {
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
