namespace cs2.core {
    public enum VariablePath {
        Unknown,
        Static,
        ClassVariable,
        FunctionInParameter,
        FunctionStack,
        Return
    }
    
    public struct ExpressionResult {
        public bool Processed { get; set; }
        public List<string> BeforeLines { get; set; }
        public List<string> AfterLines { get; set; }
        public VariablePath VarPath { get; set; }

        public ConversionClass Class { get; set; }
        public VariableType Type { get; set; }
        public ConversionVariable Variable { get; set; }

        public ExpressionResult(
            bool processed,
            VariablePath varPath = VariablePath.Unknown,
            VariableType type = null,
            List<string> beforeLines = null,
            List<string> afterLines = null
            ) {
            Processed = processed;
            VarPath = varPath;
            Type = type;
            BeforeLines = beforeLines;
            AfterLines = afterLines;
        }

        public override string ToString() {
            return $"{Processed}, {Type}";
        }
    }
}
