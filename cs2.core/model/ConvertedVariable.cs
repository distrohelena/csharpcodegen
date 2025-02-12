using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.core {
    public class ConvertedVariable {
        public string Name { get; set; }
        public string Remap { get; set; }

        public ConvertedVariableType VarType {
            get { return varType; }
            set {
                varType = value;
                SetDefaultAssignment();
            }
        }

        public MemberAccessType AccessType { get; set; }
        public MemberDeclarationType DeclarationType { get; set; }

        public bool IsAbstract { get; set; }
        public bool IsStatic { get; set; }
        public bool IsOverride { get; set; }
        public bool IsGet { get; set; }
        public bool IsSet { get; set; }

        public string? Assignment { get; set; }
        public string? DefaultValue { get; set; }

        public BlockSyntax? GetBlock { get; set; }
        public BlockSyntax? SetBlock { get; set; }
        public ExpressionSyntax? ArrowExpression { get; set; }

        private ConvertedVariableType varType;

        public ConvertedVariable() {
            Name = "";
            varType = new ConvertedVariableType();
        }

        public override string ToString() {
            return Name;
        }

        public void SetDefaultAssignment() {
            if (!string.IsNullOrEmpty(Assignment)) {
                return;
            }

            if (Utils.IsNumber(VarType.TypeName)) {
                Assignment = "0";
            } else if (Utils.IsBoolean(VarType.TypeName)) {
                Assignment = "false";
            }
        }
    }
}
