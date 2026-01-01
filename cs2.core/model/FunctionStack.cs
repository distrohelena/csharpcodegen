namespace cs2.core {
    public class FunctionStack {
        public ConversionFunction Function { get; set; }
        public List<ConversionVariable> Stack { get; set; }

        public FunctionStack(ConversionFunction function) {
            Function = function;
            Stack = new List<ConversionVariable>();
        }

        public override string ToString() {
            return Function.ToString();
        }
    }
}
