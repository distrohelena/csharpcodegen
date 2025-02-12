namespace cs2.core {
    public class ConversionContext {
        public ConvertedProgram Program { get; private set; }
        public ConvertedClass CurrentClass { get; private set; }
        public ConvertedVariable CurrentVar { get; private set; }
        public ConvertedFunction CurrentFunction { get; private set; }

        private List<ConvertedClass> classes;

        public ConversionContext(ConvertedProgram program) {
            Program = program;
            classes = new List<ConvertedClass>();
        }

        public void AssignFunction(ConvertedFunction fn) {
            CurrentFunction = fn;
        }

        public void AssignClass(ConvertedClass fn) {
            CurrentClass = fn;
        }

        public ConvertedClass StartClass() {
            CurrentVar = null;
            CurrentFunction = null;

            CurrentClass = new ConvertedClass();
            Program.Classes.Add(CurrentClass);
            classes.Add(CurrentClass);
            return CurrentClass;
        }

        public void PopClass() {
            classes.RemoveAt(classes.Count - 1);

            CurrentVar = null;
            CurrentFunction = null;
            if (classes.Count > 0) {
                CurrentClass = classes[classes.Count - 1];
            } else {
                CurrentClass = null;
            }
        }

        public ConvertedVariable StartVar() {
            if (CurrentClass == null) {
                throw new NotSupportedException();
            }

            CurrentVar = new ConvertedVariable();
            CurrentClass.Variables.Add(CurrentVar);
            return CurrentVar;
        }

        public ConvertedFunction StartFn() {
            if (CurrentClass == null) {
                throw new NotSupportedException();
            }

            CurrentFunction = new ConvertedFunction();
            CurrentClass.Functions.Add(CurrentFunction);
            return CurrentFunction;
        }
    }
}
