using Microsoft.CodeAnalysis;

namespace cs2.core {
    public class ConvertedClass {
        public string Name { get; set; }

        public MemberDeclarationType DeclarationType { get; set; }

        public bool IsNative { get; set; }
        public List<string> Extensions { get; set; }

        public List<ConvertedVariable> Variables { get; set; }
        public List<ConvertedFunction> Functions { get; set; }

        public List<object>? EnumMembers { get; set; }
        public List<string>? GenericArgs { get; set; }

        public SemanticModel Semantic { get; set; }

        public ConvertedClass() {
            Name = string.Empty;
            DeclarationType = MemberDeclarationType.Class;
            Variables = new List<ConvertedVariable>();
            Functions = new List<ConvertedFunction>();
            Extensions = new List<string>();
        }

        public string GetGenericArguments() {
            string generic = "";
            if (GenericArgs != null) {
                generic = "<";
                for (int k = 0; k < GenericArgs.Count; k++) {
                    string parameter = GenericArgs[k];
                    if (k == GenericArgs.Count - 1) {
                        generic += parameter;
                    } else {
                        generic += parameter + ", ";
                    }
                }
                generic += ">";
            }

            return generic;
        }

        public override string ToString() {
            return Name;
        }
    }
}
