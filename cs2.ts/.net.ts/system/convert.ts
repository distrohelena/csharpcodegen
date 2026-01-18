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

    static toBoolean(value: any): boolean {
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
