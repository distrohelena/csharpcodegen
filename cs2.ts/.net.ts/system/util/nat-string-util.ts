// @ts-nocheck
﻿export class NativeStringUtil {
    static isNullOrEmpty(value?: string | null): boolean {
        return !value || value.length === 0;
    }
}