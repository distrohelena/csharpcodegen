// @ts-nocheck
type EnumRegistryEntry = {
    EnumObject: any;
    FullName: string;
    Name: string;
};

export class Enum {
    private static Registry: EnumRegistryEntry[] = [];

    public static RegisterEnum(enumObject: any, metadata: { fullName?: string; name?: string } | null | undefined): void {
        if (enumObject == null) {
            return;
        }
        const enumType = typeof enumObject;
        if (enumType !== "object" && enumType !== "function") {
            return;
        }
        const fullName = metadata?.fullName ?? "";
        const name = metadata?.name ?? "";
        for (let i = 0; i < Enum.Registry.length; i++) {
            const entry = Enum.Registry[i];
            if (entry.EnumObject === enumObject) {
                entry.FullName = fullName || entry.FullName;
                entry.Name = name || entry.Name;
                return;
            }
            if (fullName && entry.FullName === fullName) {
                entry.EnumObject = enumObject;
                entry.Name = name || entry.Name;
                return;
            }
        }
        Enum.Registry.push({ EnumObject: enumObject, FullName: fullName, Name: name });
    }

    public static IsDefined(enumType: any, value: any): boolean {
        if (enumType == null || enumType.IsEnum !== true) {
            return false;
        }
        const enumObject = enumType._ctor;
        return Enum.IsDefinedOnObject(enumObject, value);
    }

    public static TryParse(value: any, ignoreCase: boolean, outValue: { value: any }, enumType?: any): boolean {
        outValue.value = null;
        if (value == null) {
            return false;
        }
        const text = String(value).trim();
        if (!text) {
            return false;
        }
        const numeric = Number(text);
        if (!Number.isNaN(numeric)) {
            outValue.value = numeric;
            return true;
        }
        if (enumType != null && Enum.TryParseOnObject(enumType._ctor, text, ignoreCase, outValue)) {
            return true;
        }
        for (let i = 0; i < Enum.Registry.length; i++) {
            if (Enum.TryParseOnObject(Enum.Registry[i].EnumObject, text, ignoreCase, outValue)) {
                return true;
            }
        }
        outValue.value = null;
        return false;
    }

    private static IsDefinedOnObject(enumObject: any, value: any): boolean {
        if (enumObject == null) {
            return false;
        }
        if (typeof value === "number") {
            return Object.prototype.hasOwnProperty.call(enumObject, value);
        }
        const text = String(value).trim();
        if (!text) {
            return false;
        }
        if (Object.prototype.hasOwnProperty.call(enumObject, text)) {
            return true;
        }
        const numeric = Number(text);
        if (!Number.isNaN(numeric)) {
            return Object.prototype.hasOwnProperty.call(enumObject, numeric);
        }
        return false;
    }

    private static TryParseOnObject(enumObject: any, text: string, ignoreCase: boolean, outValue: { value: any }): boolean {
        if (enumObject == null) {
            return false;
        }
        if (Object.prototype.hasOwnProperty.call(enumObject, text)) {
            outValue.value = enumObject[text];
            return true;
        }
        if (!ignoreCase) {
            return false;
        }
        const normalized = text.toLowerCase();
        const keys = Object.keys(enumObject);
        for (let i = 0; i < keys.length; i++) {
            const key = keys[i];
            if (key.toLowerCase() === normalized) {
                outValue.value = enumObject[key];
                return true;
            }
        }
        return false;
    }
}
