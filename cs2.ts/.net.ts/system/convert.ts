// @ts-nocheck
﻿export class Convert {

    static fromBase64String(base64: string): Uint8Array {
        if (typeof atob === "function") {
            // Browser
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
            }
            return bytes;
        } else {
            // Node.js
            return Uint8Array.from(Buffer.from(base64, "base64"));
        }
    }

    static toBase64String(bytes: Uint8Array): string {
        if (typeof btoa === "function") {
            // Browser
            let binary = "";
            for (let i = 0; i < bytes.length; i++) {
                binary += String.fromCharCode(bytes[i]);
            }
            return btoa(binary);
        } else {
            // Node.js
            return Buffer.from(bytes).toString("base64");
        }
    }

    /** Accepts the CLR provider overload; primitive Boolean conversion is culture independent. */
    static toBoolean(value: any, _provider?: any): boolean {
        if (value === null || value === undefined) {
            return false;
        }
        if (typeof value === "boolean") {
            return value;
        }
        if (typeof value === "number") {
            return value !== 0;
        }
        if (typeof value === "bigint") {
            return value !== 0n;
        }
        if (typeof value === "string") {
            const text = value.trim();
            if (text.length === 0) {
                throw new Error("String was not recognized as a valid Boolean.");
            }
            const lower = text.toLowerCase();
            if (lower === "true") {
                return true;
            }
            if (lower === "false") {
                return false;
            }
            throw new Error("String was not recognized as a valid Boolean.");
        }

        throw new Error("Invalid cast to Boolean.");
    }

    static toDouble(value: any, _provider?: any): number {
        if (value === null || value === undefined) {
            return 0;
        }
        if (typeof value === "number") {
            return value;
        }
        if (typeof value === "boolean") {
            return value ? 1 : 0;
        }
        if (typeof value === "bigint") {
            return Number(value);
        }
        if (typeof value === "string") {
            const text = value.trim();
            if (text.length === 0) {
                throw new Error("String was not recognized as a valid number.");
            }
            const parsed = Number(text);
            if (Number.isNaN(parsed)) {
                throw new Error("String was not recognized as a valid number.");
            }
            return parsed;
        }

        throw new Error("Invalid cast to Double.");
    }

    static toInt32(value: any, _provider?: any): number {
        if (value === null || value === undefined) {
            return 0;
        }
        if (typeof value === "boolean") {
            return value ? 1 : 0;
        }
        if (typeof value === "number") {
            return Convert.toInt32FromNumber(value);
        }
        if (typeof value === "bigint") {
            const numeric = Number(value);
            return Convert.toInt32FromNumber(numeric);
        }
        if (typeof value === "string") {
            const text = value.trim();
            if (text.length === 0) {
                throw new Error("String was not recognized as a valid Int32.");
            }
            if (!/^[+-]?\d+$/.test(text)) {
                throw new Error("String was not recognized as a valid Int32.");
            }
            const parsed = Number(text);
            return Convert.toInt32FromNumber(parsed);
        }

        throw new Error("Invalid cast to Int32.");
    }

    /**
     * Converts the browser representation of a supported value to the .NET string representation.
     * The provider is accepted because generated calls preserve the .NET overload, while browser
     * conversion uses the invariant JavaScript representation for primitive values.
     */
    static ToString(value: any, _provider?: any): string {
        if (value === null || value === undefined) {
            return "";
        }
        if (typeof value === "boolean") {
            return value ? "True" : "False";
        }
        if (typeof value === "string") {
            return value;
        }
        if (typeof value === "number" || typeof value === "bigint") {
            return String(value);
        }
        return String(value);
    }

    /**
     * Converts supported primitive browser values to the numeric representation used for .NET Int64.
     * JavaScript numbers cannot represent every Int64 exactly, so callers retain the existing browser
     * number representation while textual inputs still receive exact range validation before conversion.
     */
    static ToInt64(value: any, _provider?: any): number {
        if (value === null || value === undefined) {
            return 0;
        }
        if (typeof value === "boolean") {
            return value ? 1 : 0;
        }
        if (typeof value === "number") {
            return Convert.toInt64FromNumber(value);
        }
        if (typeof value === "bigint") {
            Convert.validateInt64(value);
            return Number(value);
        }
        if (typeof value === "string") {
            const text = value.trim();
            if (!/^[+-]?\d+$/.test(text)) {
                throw new Error("String was not recognized as a valid Int64.");
            }
            const parsed = BigInt(text);
            Convert.validateInt64(parsed);
            return Number(parsed);
        }
        throw new Error("Invalid cast to Int64.");
    }

    /** Converts a finite browser number using .NET midpoint-to-even behavior and the Int64 range. */
    private static toInt64FromNumber(value: number): number {
        if (!Number.isFinite(value)) {
            throw new Error("Value was either too large or too small for an Int64.");
        }
        const rounded = Convert.roundToEven(value);
        if (rounded < -9223372036854775808 || rounded >= 9223372036854775808) {
            throw new Error("Value was either too large or too small for an Int64.");
        }
        return rounded;
    }

    /** Validates a precise Int64 value before converting it to the browser number representation. */
    private static validateInt64(value: bigint): void {
        if (value < -9223372036854775808n || value > 9223372036854775807n) {
            throw new Error("Value was either too large or too small for an Int64.");
        }
    }

    private static toInt32FromNumber(value: number): number {
        if (!Number.isFinite(value)) {
            throw new Error("Value was either too large or too small for an Int32.");
        }
        const rounded = Convert.roundToEven(value);
        if (rounded < -2147483648 || rounded > 2147483647) {
            throw new Error("Value was either too large or too small for an Int32.");
        }
        return rounded;
    }

    private static roundToEven(value: number): number {
        const truncated = Math.trunc(value);
        const fraction = Math.abs(value - truncated);
        if (fraction > 0.5) {
            return truncated + Math.sign(value);
        }
        if (fraction < 0.5) {
            return truncated;
        }
        if (truncated % 2 !== 0) {
            return truncated + Math.sign(value);
        }
        return truncated;
    }
}

declare global {
    interface BooleanConstructor {
        TryParse(value: string | null | undefined, outValue: { value: boolean }): boolean;
    }
    interface NumberConstructor {
        TryParse(value: string | null | undefined, outValue: { value: number }): boolean;
        TryParse(value: string | null | undefined, style: any, provider: any, outValue: { value: number }): boolean;
    }
}

if (typeof Boolean.TryParse !== "function") {
    Boolean.TryParse = (value: string | null | undefined, outValue: { value: boolean }): boolean => {
        const text = value == null ? "" : value.trim().toLowerCase();
        if (text === "true" || text === "false") {
            outValue.value = text === "true";
            return true;
        }
        outValue.value = false;
        return false;
    };
}

if (typeof Number.TryParse !== "function") {
    Number.TryParse = (value: string | null | undefined, styleOrOutValue: any, _provider?: any, suppliedOutValue?: { value: number }): boolean => {
        // C# emits both TryParse(text, out value) and TryParse(text, style, provider, out value).
        // Style/provider are intentionally ignored by this bounded invariant numeric parser.
        const outValue = suppliedOutValue ?? styleOrOutValue as { value: number };
        if (value == null) {
            outValue.value = 0;
            return false;
        }
        const text = String(value).trim();
        if (!text) {
            outValue.value = 0;
            return false;
        }
        if (!/^[+-]?\d+$/.test(text)) {
            outValue.value = 0;
            return false;
        }
        const parsed = Number(text);
        if (!Number.isSafeInteger(parsed)) {
            outValue.value = 0;
            return false;
        }
        outValue.value = parsed;
        return true;
    };
}
