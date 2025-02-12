namespace cs2.core {
    public static class BinaryOpTypesExtensions {
        public static string ToStringOperator(this BinaryOpTypes op) {
            return op switch {
                // Math operators
                BinaryOpTypes.Plus => "+",
                BinaryOpTypes.Minus => "-",
                BinaryOpTypes.Divide => "/",
                BinaryOpTypes.Multiply => "*",
                BinaryOpTypes.Modulo => "%",

                // Comparison operators
                BinaryOpTypes.GreaterThan => ">",
                BinaryOpTypes.GreaterThanOrEqual => ">=",
                BinaryOpTypes.LessThan => "<",
                BinaryOpTypes.LessThanOrEqual => "<=",
                BinaryOpTypes.Equal => "==",
                BinaryOpTypes.NotEqual => "!=",

                // Boolean operators
                BinaryOpTypes.BinAnd => "&&",
                BinaryOpTypes.BinOr => "||",
                BinaryOpTypes.BinNot => "!",
                BinaryOpTypes.Coalesce => "??",

                // Type-checking operators
                BinaryOpTypes.InstanceOf => "instanceof",
                BinaryOpTypes.As => "as",

                // Bitwise operators
                BinaryOpTypes.BitwiseAnd => "&",
                BinaryOpTypes.BitwiseNot => "~",
                BinaryOpTypes.BitwiseOr => "|",
                BinaryOpTypes.RightShift => ">>",
                BinaryOpTypes.LeftShift => "<<",
                BinaryOpTypes.ExclusiveOr => "^",

                // Default case
                _ => throw new Exception("Unknown binary operator")
            };
        }
    }
}
