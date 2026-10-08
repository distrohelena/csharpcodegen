/** Bit primitives used by translated unsigned 32-bit routing distance calculations. */
export class BitOperations {
    /** Counts leading zeros in the uint32 representation, returning 32 for zero. */
    static LeadingZeroCount(value: number): number {
        return Math.clz32(value);
    }
}
