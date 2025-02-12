namespace cs2.core {
    public enum BinaryOpTypes {
        // math
        Plus, Minus, Divide, Multiply, Modulo,
        // comparison
        GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Equal, NotEqual,
        // boolean
        BinAnd, BinOr, BinNot,

        InstanceOf, As,

        Coalesce,

        // Bitwise,
        BitwiseAnd, BitwiseNot, BitwiseOr,
        RightShift, LeftShift,
        ExclusiveOr,

        Unknown
    }
}
