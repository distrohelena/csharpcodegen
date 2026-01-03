export class NativeStringUtil {
    static readonly Empty: string = "";

    static isNullOrEmpty(value?: string | null): boolean {
        return !value || value.length === 0;
    }

    static isNullOrWhiteSpace(value?: string | null): boolean {
        return value == null || value.trim().length === 0;
    }

    static toCamelCase(value?: string | null): string {
        if (!value || value.length === 0) {
            return value ?? "";
        }
        if (value.length === 1) {
            return value.toLowerCase();
        }
        return value[0].toLowerCase() + value.substring(1);
    }
}
