using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.core {
    public class ConversionFunction {
        public string Name { get; set; }
        public string Remap { get; set; }
        public string RemapClass { get; set; }
        /// <summary>
        /// Gets or sets one stable Roslyn-derived source-method identity used by backend emitters that need to specialize specific generated functions.
        /// </summary>
        public string SourceMethodKey { get; set; }

        /// <summary>
        /// Gets or sets the maintained C# declaration identity for this function, or <c>null</c> when the function was explicitly synthesized without a source declaration.
        /// </summary>
        public ConversionSourceLocation SourceLocation { get; set; }

        public List<string>? GenericParameters { get; set; }

        public MemberAccessType AccessType { get; set; }
        public MemberDeclarationType DeclarationType { get; set; }

        public List<string> Flags { get; set; }

        public bool IsStatic { get; set; }
        public bool IsAsync { get; set; }
        public bool IsConstructor { get; set; }
        public bool IsOverride { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the generated native signature should return a constant reference instead of a value copy.
        /// </summary>
        public bool ReturnsConstReference { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the generated native signature should return a mutable reference instead of a value copy.
        /// </summary>
        public bool ReturnsReference { get; set; }
        public string NativeFreeFunctionName { get; set; }
        public string NativeFreeFunctionIncludePath { get; set; }
        /// <summary>
        /// Gets or sets the documentation-comment id of the original method definition, used to match this function
        /// against P/Invoke plan entries; empty when the function has no resolved method symbol.
        /// </summary>
        public string MethodId { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the source method is a <c>DllImport</c> extern declaration, which is
        /// lowered to a direct native call and therefore never emitted as a generated member or throwing stub.
        /// </summary>
        public bool IsDllImport { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the source method carries <c>UnmanagedCallersOnly</c>, meaning native
        /// code may call it through a generated callback trampoline.
        /// </summary>
        public bool IsUnmanagedCallersOnly { get; set; }
        public SemanticModel Semantic { get; set; }

        public List<ConversionVariable> InParameters { get; set; }

        public VariableType? ReturnType { get; set; }

        /// <summary>
        /// Analyzes all return value statements and returns their exact types.
        /// For example, in TypeScript we would normally return an Array<number>, but
        /// we want an UInt8Array, and we can see trough the returned type (MemoryStream is correctly implemented outside)
        /// </summary>
        public List<VariableType>? AnalyzedReturns { get; set; }

        public BlockSyntax? RawBlock { get; set; }
        public ArrowExpressionClauseSyntax? ArrowExpression { get; set; }
        public ConstructorInitializerSyntax? ConstructorInitializer { get; set; }
        public bool AsyncAnalyzed { get; set; }

        public bool HasBody {
            get {
                return RawBlock != null || ArrowExpression != null;
            }
        }

        public List<ConversionFunctionVariableUsage> BodyVariables { get; set; }

        public ConversionFunction() {
            Name = "";
            SourceMethodKey = string.Empty;
            InParameters = new List<ConversionVariable>();
            BodyVariables = new List<ConversionFunctionVariableUsage>();
            NativeFreeFunctionName = string.Empty;
            NativeFreeFunctionIncludePath = string.Empty;
            MethodId = string.Empty;
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
