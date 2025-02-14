namespace cs2.core {
    public struct ExpressionResult {
        public bool Processed { get; set; }
        public VariableType Type { get; set; }
        public List<string> BeforeLines { get; set; }
        public List<string> AfterLines { get; set; }

        public ExpressionResult(
            bool processed,
            VariableType type = null,
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
