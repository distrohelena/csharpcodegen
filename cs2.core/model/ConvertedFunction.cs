using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.core {
    public class ConvertedFunction {
        public string Name { get; set; }
        public string Remap { get; set; }

        public List<string>? GenericParameters { get; set; }

        public MemberAccessType AccessType { get; set; }
        public MemberDeclarationType DeclarationType { get; set; }

        public bool IsStatic { get; set; }
        public bool IsAsync { get; set; }
        public bool IsConstructor { get; set; }

        public List<ConvertedVariable> InParameters { get; set; }

        public ConvertedVariableType? ReturnType { get; set; }

        public BlockSyntax? RawBlock { get; set; }
        public ArrowExpressionClauseSyntax? ArrowExpression { get; set; }

        public bool HasBody {
            get {
                return RawBlock != null || ArrowExpression != null;
            }
        }

        public ConvertedFunction() {
            Name = "";
            InParameters = new List<ConvertedVariable>();
        }

        public string GetGenericArguments() {
            string generic = "";
            if (GenericParameters != null) {
                generic = "<";
                for (int k = 0; k < GenericParameters.Count; k++) {
                    string parameter = GenericParameters[k];
                    if (k == GenericParameters.Count - 1) {
                        generic += parameter;
                    } else {
                        generic += parameter + ", ";
                    }
                }
                generic += ">";
            }

            return generic;
        }

        public string GetClassType() {
            string type = "";
            if (DeclarationType == MemberDeclarationType.Abstract) {
                type = "abstract ";
            }

            return type;
        }

        public string GetAsync() {
            if (IsAsync) {
                return "async ";
            }
            return "";
        }

        public override string ToString() {
            return Name;
        }
    }
}
