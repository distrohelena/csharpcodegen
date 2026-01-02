// @ts-nocheck
﻿export class NativeStringUtil {
    static readonly Empty: string = "";

    static isNullOrEmpty(value?: string | null): boolean {
        return !value || value.length === 0;
    }

    static isNullOrWhiteSpace(value?: string | null): boolean {
        return value == null || value.trim().length === 0;
    }
}
