namespace cs2.cpp {
    /// <summary>Immutable numeric representation contract shared by generated Core and native modules.</summary>
    public sealed class CPPRealNumericPolicy {
        /// <summary>Creates a representation choice without implying that a native backend implements it.</summary>
        /// <param name="storageBits">IEEE storage width.</param>
        /// <param name="arithmeticBits">IEEE arithmetic width.</param>
        public CPPRealNumericPolicy(int storageBits, int arithmeticBits) {
            if (storageBits != 16 && storageBits != 32 && storageBits != 64) {
                throw new ArgumentOutOfRangeException(nameof(storageBits), "Real storage bits must be 16, 32 or 64.");
            } else if (arithmeticBits != 16 && arithmeticBits != 32 && arithmeticBits != 64) {
                throw new ArgumentOutOfRangeException(nameof(arithmeticBits), "Real arithmetic bits must be 16, 32 or 64.");
            }
            StorageBits = storageBits;
            ArithmeticBits = arithmeticBits;
        }

        /// <summary>Gets the selected stored scalar width.</summary>
        public int StorageBits { get; }

        /// <summary>Gets the selected expression evaluation width.</summary>
        public int ArithmeticBits { get; }

        /// <summary>Gets the engine-compatible numeric representation version.</summary>
        public int RepresentationVersion => 1;

        /// <summary>Gets the canonical identity included in generated runtime contracts.</summary>
        public string Identity => $"real-v{RepresentationVersion}-s{StorageBits}-a{ArithmeticBits}";
    }
}
