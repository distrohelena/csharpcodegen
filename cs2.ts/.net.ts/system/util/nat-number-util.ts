/** Numeric comparison support for primitive C# values represented as JavaScript numbers. */
export class NativeNumberUtil {
    /** Returns the ordering sign, including .NET ordering of NaN before every non-NaN value. */
    public static compareTo(left: number, right: number): number {
        if (left < right) return -1;
        if (left > right) return 1;
        if (left === right) return 0;
        if (Number.isNaN(left)) return Number.isNaN(right) ? 0 : -1;
        return 1;
    }
}
