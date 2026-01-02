using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.core {
    public class ConversionVariable {
        public string Name { get; set; }
        public string Remap { get; set; }
        /// <summary>
        /// Gets or sets the class name to emit when remapping static member access.
        /// </summary>
        public string RemapClass { get; set; }

        public VariableType VarType {
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
        public ParameterModifier Modifier { get; set; }

        public string? Assignment { get; set; }
        public string? DefaultValue { get; set; }

        public BlockSyntax? GetBlock { get; set; }
        public BlockSyntax? SetBlock { get; set; }
        public ExpressionSyntax? ArrowExpression { get; set; }

        VariableType varType;

        public ConversionVariable() {
            Name = "";
            varType = new VariableType();
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
