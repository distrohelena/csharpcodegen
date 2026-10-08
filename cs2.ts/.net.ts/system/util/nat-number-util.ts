import { FormatException } from "../format.exception";

/** Numeric comparison support for primitive C# values represented as JavaScript numbers. */
export class NativeNumberUtil {
    /** Bounds a numeric value using .NET Math.Clamp ordering and endpoint behavior. */
    public static clamp(value: number, min: number, max: number): number {
        if (min > max) {
            throw new Error("min must be less than or equal to max.");
        }
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /** Returns the ordering sign, including .NET ordering of NaN before every non-NaN value. */
    public static compareTo(left: number, right: number): number {
        if (left < right) return -1;
        if (left > right) return 1;
        if (left === right) return 0;
        if (Number.isNaN(left)) return Number.isNaN(right) ? 0 : -1;
        return 1;
    }

    /** Parses a C# integral primitive without accepting fractional or lossy JavaScript numbers. */
    public static tryParseInteger(value: string | null | undefined, outValue: { value: number }, minimum: number, maximum: number): boolean {
        if (value == null || !/^[+-]?\d+$/.test(value.trim())) {
            outValue.value = 0;
            return false;
        }
        const parsed = Number(value.trim());
        if (!Number.isSafeInteger(parsed) || parsed < minimum || parsed > maximum) {
            outValue.value = 0;
            return false;
        }
        outValue.value = parsed;
        return true;
    }

    /** Parses a bounded CLR integral primitive and throws when the text is invalid or outside its range. */
    public static parseInteger(value: string | null | undefined, minimum: number, maximum: number): number {
        const parsed = { value: 0 };
        if (!NativeNumberUtil.tryParseInteger(value, parsed, minimum, maximum)) {
            throw new FormatException("Input string was not in a correct format.");
        }
        return parsed.value;
    }

    /** Evaluates format arguments emitted by C# while using the browser's invariant numeric representation. */
    public static tryParseIntegerWithFormat(value: string | null | undefined, _style: unknown, _provider: unknown, outValue: { value: number }, minimum: number, maximum: number): boolean {
        return NativeNumberUtil.tryParseInteger(value, outValue, minimum, maximum);
    }

    /** Parses finite invariant floating-point text for C# float, double, and decimal emissions. */
    public static tryParseFloatingPoint(value: string | null | undefined, outValue: { value: number }): boolean {
        const text = value == null ? "" : value.trim();
        if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(text)) {
            outValue.value = 0;
            return false;
        }
        const parsed = Number(text);
        if (!Number.isFinite(parsed)) {
            outValue.value = 0;
            return false;
        }
        outValue.value = parsed;
        return true;
    }

    /** Format/provider arguments remain evaluated by generated code; invariant browser parsing is delegated to the core helper. */
    public static tryParseFloatingPointWithFormat(value: string | null | undefined, _style: unknown, _provider: unknown, outValue: { value: number }): boolean {
        return NativeNumberUtil.tryParseFloatingPoint(value, outValue);
    }
}
