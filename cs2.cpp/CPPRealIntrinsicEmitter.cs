using cs2.core;
using Microsoft.CodeAnalysis;

namespace cs2.cpp {
    /// <summary>Emits engine scalar intrinsics by complete semantic type identity without changing primitive type mappings.</summary>
    public static class CPPRealIntrinsicEmitter {
        /// <summary>Emits reviewed native support for the configurable scalar surface.</summary>
        /// <param name="type">Source type being lowered.</param>
        /// <param name="header">Generated type header writer.</param>
        /// <param name="source">Generated type source writer.</param>
        /// <param name="processor">Conversion host that records native runtime dependencies.</param>
        /// <returns>True only for the explicitly identified engine intrinsic types.</returns>
        public static bool TryEmitClass(ConversionClass type, TextWriter header, TextWriter source, CPPConversiorProcessor processor) {
            string identity = type.TypeSymbol?.ToDisplayString() ?? string.Empty;
            if (identity != "helengine.Real" && identity != "helengine.RealEvaluation" && identity != "helengine.RealMath"
                && identity != "helengine.RealRepresentation") { return false; }
            processor.RegisterRuntimeRequirement("NativeReal");
            if (processor.Options.RealNumericPolicy.ArithmeticBits == 16) { processor.RegisterRuntimeRequirement("NativeBinary16Arithmetic"); }
            header.WriteLine("#pragma once");
            header.WriteLine("#include \"runtime/native_real.hpp\"");
            if (identity == "helengine.RealRepresentation") {
                header.WriteLine("class NumericPrecisionPolicy;");
                header.WriteLine("struct RealRepresentation {");
                header.WriteLine("static constexpr int32_t StorageBits = HE_CPP_REAL_STORAGE_BITS;");
                header.WriteLine("static constexpr int32_t ArithmeticBits = HE_CPP_REAL_ARITHMETIC_BITS;");
                header.WriteLine("static NumericPrecisionPolicy* get_Policy();");
                header.WriteLine("};");
                source.WriteLine("#include \"RealRepresentation.hpp\"");
                source.WriteLine("#include \"NumericPrecisionPolicy.hpp\"");
                source.WriteLine("NumericPrecisionPolicy* RealRepresentation::get_Policy() {");
                source.WriteLine("static NumericPrecisionPolicy policy(HE_CPP_REAL_STORAGE_BITS, HE_CPP_REAL_ARITHMETIC_BITS, HE_CPP_REAL_REPRESENTATION_VERSION);");
                source.WriteLine("return &policy;");
                source.WriteLine("}");
            }
            return true;
        }

        /// <summary>Lowers representation constants and the policy getter independently of managed auto-property optimization.</summary>
        /// <param name="symbol">Semantically resolved source member.</param>
        /// <param name="expression">Native build-policy expression when the member is recognized.</param>
        /// <returns>True only for the engine representation's reviewed native members.</returns>
        public static bool TryGetRepresentationConstant(ISymbol symbol, out string expression) {
            expression = string.Empty;
            if (symbol?.ContainingType?.ToDisplayString() != "helengine.RealRepresentation") { return false; }
            if (symbol is IPropertySymbol property && property.IsStatic && property.Name == "Policy") {
                expression = "::RealRepresentation::get_Policy()";
            } else if (symbol is IFieldSymbol field && field.Name == "StorageBits") { expression = "HE_CPP_REAL_STORAGE_BITS"; }
            else if (symbol is IFieldSymbol arithmeticField && arithmeticField.Name == "ArithmeticBits") { expression = "HE_CPP_REAL_ARITHMETIC_BITS"; }
            return expression.Length != 0;
        }
    }
}
