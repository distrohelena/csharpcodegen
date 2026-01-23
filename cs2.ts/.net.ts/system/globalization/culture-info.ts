// @ts-nocheck
import { TextInfo } from "./text-info";

export class CultureInfo {
    public static readonly InvariantCulture: CultureInfo = new CultureInfo("Invariant Culture");
    public static CurrentCulture: CultureInfo = CultureInfo.InvariantCulture;

    public readonly Name: string;
    public readonly TextInfo: TextInfo;

    constructor(name?: string) {
        this.Name = name ?? "Invariant Culture";
        this.TextInfo = new TextInfo(this.Name);
    }

    public static CreateSpecificCulture(name: string): CultureInfo {
        return new CultureInfo(name);
    }
}

export enum NumberStyles {
    None = 0,
    AllowLeadingWhite = 1,
    AllowTrailingWhite = 2,
    AllowLeadingSign = 4,
    Integer = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign
}

declare global {
    var NumberStyles: typeof NumberStyles;
}

if (typeof globalThis !== "undefined" && !(globalThis as any).NumberStyles) {
    (globalThis as any).NumberStyles = NumberStyles;
}

declare global {
    interface Boolean {
        toString(provider?: any): string;
        ToString(provider?: any): string;
    }
}

function isCultureInfo(value: any): value is CultureInfo {
    return value instanceof CultureInfo;
}

function formatFixed(value: number, digits: number): string {
    if (!Number.isFinite(value)) {
        return String(value);
    }
    return value.toFixed(digits);
}

function trimTrailingZeros(value: string): string {
    if (value.indexOf(".") === -1) {
        return value;
    }
    let trimmed = value.replace(/0+$/, "");
    trimmed = trimmed.replace(/\.$/, "");
    return trimmed;
}

function formatNumberWithPattern(value: number, format: string, originalToString: (radix?: number) => string): string {
    const upper = format.toUpperCase();
    if (upper == "R" || upper == "G") {
        return originalToString.call(value);
    }
    if (upper.startsWith("F")) {
        const digits = parseInt(format.slice(1), 10);
        if (!Number.isNaN(digits)) {
            return formatFixed(value, digits);
        }
    }
    const decimalIndex = format.indexOf(".");
    if (format.startsWith("0.") && decimalIndex >= 0) {
        const decimals = format.length - decimalIndex - 1;
        if (decimals > 0) {
            return trimTrailingZeros(formatFixed(value, decimals));
        }
    }
    return originalToString.call(value);
}

function formatNumber(value: number, formatOrRadix?: any, provider?: any, originalToString?: (radix?: number) => string): string {
    const toStringImpl = originalToString ?? Number.prototype.toString;
    if (formatOrRadix == null || isCultureInfo(formatOrRadix)) {
        return toStringImpl.call(value);
    }
    if (typeof formatOrRadix === "number") {
        return toStringImpl.call(value, formatOrRadix);
    }
    if (typeof formatOrRadix === "string") {
        const trimmed = formatOrRadix.trim();
        if (trimmed.length === 0) {
            return toStringImpl.call(value);
        }
        if (/^[0-9]+$/.test(trimmed)) {
            const radix = Number(trimmed);
            if (radix >= 2 && radix <= 36) {
                return toStringImpl.call(value, radix);
            }
        }
        return formatNumberWithPattern(value, trimmed, toStringImpl);
    }
    if (provider != null && isCultureInfo(provider)) {
        return toStringImpl.call(value);
    }
    return toStringImpl.call(value);
}

const numberPatchKey = "__cs2tsNumberToStringPatched";
if (!(Number.prototype as any)[numberPatchKey]) {
    const originalToString = Number.prototype.toString;
    (Number.prototype as any).toString = function (formatOrRadix?: any, provider?: any): string {
        return formatNumber(this.valueOf(), formatOrRadix, provider, originalToString);
    };
    (Number.prototype as any).ToString = function (formatOrRadix?: any, provider?: any): string {
        return formatNumber(this.valueOf(), formatOrRadix, provider, originalToString);
    };
    (Number.prototype as any)[numberPatchKey] = true;
}

const booleanPatchKey = "__cs2tsBooleanToStringPatched";
if (!(Boolean.prototype as any)[booleanPatchKey]) {
    const formatBool = function (): string {
        return this.valueOf() ? "True" : "False";
    };
    (Boolean.prototype as any).toString = function (_provider?: any): string {
        return formatBool.call(this);
    };
    (Boolean.prototype as any).ToString = function (_provider?: any): string {
        return formatBool.call(this);
    };
    (Boolean.prototype as any)[booleanPatchKey] = true;
}

const stringPatchKey = "__cs2tsStringToStringPatched";
if (!(String.prototype as any)[stringPatchKey]) {
    const originalToString = String.prototype.toString;
    (String.prototype as any).ToString = function (): string {
        return originalToString.call(this);
    };
    (String.prototype as any)[stringPatchKey] = true;
}
