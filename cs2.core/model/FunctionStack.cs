namespace cs2.core {
    public class FunctionStack
    {
        public ConvertedFunction Function { get; set; }
        public List<ConvertedVariable> Stack { get; set; }

        public FunctionStack(ConvertedFunction function) {
            Function = function;
            Stack = new List<ConvertedVariable>();
        }

        public override string ToString() {
            return Function.ToString();
        }
    }
}
