namespace cs2.core {
    public struct ExpressionResult {
        public bool Processed { get; set; }
        public ConvertedVariableType Type { get; set; }
        public List<string> BeforeLines { get; set; }
        public List<string> AfterLines { get; set; }

        public ExpressionResult(
            bool processed,
            ConvertedVariableType type = null,
            List<string> beforeLines = null,
            List<string> afterLines = null
            ) {
            Processed = processed;
            Type = type;
            BeforeLines = beforeLines;
            AfterLines = afterLines;
        }

        public override string ToString() {
            return $"{Processed}, {Type}";
        }
    }
}
