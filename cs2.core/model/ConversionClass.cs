using Microsoft.CodeAnalysis;

namespace cs2.core {
    public class ConversionClass {
        public string Name { get; set; }

        public MemberDeclarationType DeclarationType { get; set; }

        public bool IsNative { get; set; }
        public List<string> Extensions { get; set; }

        public List<string> ReferencedClasses { get; set; }

        public List<ConversionVariable> Variables { get; set; }
        public List<ConversionFunction> Functions { get; set; }

        public List<object>? EnumMembers { get; set; }
        public List<string>? GenericArgs { get; set; }

        public SemanticModel Semantic { get; set; }

        public ConversionClass() {
            Name = string.Empty;
            DeclarationType = MemberDeclarationType.Class;
            Variables = new List<ConversionVariable>();
            Functions = new List<ConversionFunction>();
            Extensions = new List<string>();
            ReferencedClasses = new List<string>();
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
