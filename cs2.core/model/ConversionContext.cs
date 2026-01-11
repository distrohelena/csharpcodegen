namespace cs2.core {
    public class ConversionContext {
        public ConversionProgram Program { get; private set; }
        public ConversionClass CurrentClass { get; private set; }
        public ConversionVariable CurrentVar { get; private set; }
        public ConversionFunction CurrentFunction { get; private set; }

        private List<ConversionClass> classes;

        public ConversionContext(ConversionProgram program) {
            Program = program;
            classes = new List<ConversionClass>();
        }

        /// <summary>
        /// Resets the conversion context to a clean state, clearing any active scope tracking.
        /// </summary>
        public void Reset() {
            Reset(false);
        }

        /// <summary>
        /// Resets the conversion context to a clean state, optionally preserving existing program classes.
        /// </summary>
        /// <param name="preserveProgramClasses">True to keep non-native classes already registered on the program.</param>
        public void Reset(bool preserveProgramClasses) {
            if (!preserveProgramClasses) {
                Program.Classes.RemoveAll(cl => !cl.IsNative);
            }
            classes.Clear();

            CurrentClass = null;
            CurrentVar = null;
            CurrentFunction = null;
        }

        public void AssignFunction(ConversionFunction fn) {
            CurrentFunction = fn;
        }

        public void AssignClass(ConversionClass fn) {
            CurrentClass = fn;
        }

        public ConversionClass StartClass() {
            CurrentVar = null;
            CurrentFunction = null;

            CurrentClass = new ConversionClass();
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

        public ConversionVariable StartVar() {
            if (CurrentClass == null) {
                throw new NotSupportedException();
            }

            CurrentVar = new ConversionVariable();
            CurrentClass.Variables.Add(CurrentVar);
            return CurrentVar;
        }

        public ConversionFunction StartFn() {
            if (CurrentClass == null) {
                throw new NotSupportedException();
            }

            CurrentFunction = new ConversionFunction();
            CurrentClass.Functions.Add(CurrentFunction);
            return CurrentFunction;
        }
    }
}
