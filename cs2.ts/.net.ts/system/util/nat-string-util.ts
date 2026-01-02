// @ts-nocheck
﻿export class NativeStringUtil {
    static readonly Empty: string = "";

    static isNullOrEmpty(value?: string | null): boolean {
        return !value || value.length === 0;
    }
}
