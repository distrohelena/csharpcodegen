using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.core {
    public class ConversionVariable {
        public string Name { get; set; }
        public string Remap { get; set; }
        public SemanticModel Semantic { get; set; }
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
        /// <summary>
        /// Gets or sets a value indicating whether one generated field originated from a C# const declaration.
        /// </summary>
        public bool IsConst { get; set; }
        public bool IsOverride { get; set; }
        public bool IsGet { get; set; }
        public bool IsSet { get; set; }
        public bool HasExplicitLayoutOffset { get; set; }
        public int ExplicitLayoutOffset { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether one generated property getter should return a constant native reference.
        /// </summary>
        public bool ReturnsConstReference { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether one generated property getter should return a mutable native reference.
        /// </summary>
        public bool ReturnsReference { get; set; }
        /// <summary>
        /// Gets or sets the declaration order captured during preprocessing so native emission can preserve field layout.
        /// </summary>
        public int SourceOrder { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether one ref local must lower as a pointer-backed alias because later assignments rebind the alias rather than writing through it.
        /// </summary>
        public bool IsRebindableReferenceLocal { get; set; }
        public ParameterModifier Modifier { get; set; }

        public string? Assignment { get; set; }
        public ExpressionSyntax? AssignmentExpression { get; set; }
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
