// @ts-nocheck
import { JsonIgnoreCondition } from "./json-ignore-condition";
import { JsonNamingPolicy } from "./json-naming-policy";
import { JsonNumberHandling } from "./json-number-handling";
import { JsonSerializerOptions } from "./json-serializer-options";
import { JsonStringEnumConverter } from "./serialization/json-string-enum-converter";
import { JsonWriterOptions } from "./json-writer-options";
import { Utf8JsonWriter } from "./utf8-json-writer";
import { BindingFlags, Type } from "../../../src/reflection";
import { DateTime } from "../../date-time";
import { Guid } from "../../guid";
import { Dictionary } from "../../collections/generic/dictionary";
import { List } from "../../collections/generic/list";
import { Encoding } from "../encoding";

function stripTrailingCommas(text: string): string {
    return text.replace(/,\s*([}\]])/g, "$1");
}

function applyNamingPolicy(name: string, options?: JsonSerializerOptions): string {
    const policy = options?.PropertyNamingPolicy;
    if (!policy) {
        return name;
    }
    if (typeof policy.ConvertName === "function") {
        return policy.ConvertName(name);
    }
    if (typeof policy === "function") {
        return policy(name);
    }
    return name;
}

function getJsonPropertyName(prop: any): string | null {
    if (!prop || typeof prop.GetCustomAttribute !== "function") {
        return null;
    }
    const attr = prop.GetCustomAttribute("System.Text.Json.Serialization.JsonPropertyNameAttribute")
        ?? prop.GetCustomAttribute("JsonPropertyNameAttribute");
    const args = attr?.ctorArgs;
    return Array.isArray(args) && typeof args[0] === "string" ? args[0] : null;
}

function getJsonIgnoreCondition(prop: any): JsonIgnoreCondition | null {
    if (!prop || typeof prop.GetCustomAttribute !== "function") {
        return null;
    }
    const attr = prop.GetCustomAttribute("System.Text.Json.Serialization.JsonIgnoreAttribute")
        ?? prop.GetCustomAttribute("JsonIgnoreAttribute");
    const condition = attr?.namedArgs?.Condition;
    return typeof condition === "number" ? condition : null;
}

function getJsonConverterTypeName(prop: any): string | null {
    if (!prop || typeof prop.GetCustomAttribute !== "function") {
        return null;
    }
    const attr = prop.GetCustomAttribute("System.Text.Json.Serialization.JsonConverterAttribute")
        ?? prop.GetCustomAttribute("JsonConverterAttribute");
    const args = attr?.ctorArgs;
    if (!Array.isArray(args) || args.length === 0) {
        return null;
    }
    return args[0] != null ? String(args[0]) : null;
}

function shouldIgnoreValue(value: any, condition: JsonIgnoreCondition): boolean {
    if (condition === JsonIgnoreCondition.Always) {
        return true;
    }
    if (condition === JsonIgnoreCondition.WhenWritingNull) {
        return value == null;
    }
    if (condition === JsonIgnoreCondition.WhenWritingDefault) {
        if (value == null) return true;
        if (typeof value === "boolean") return value === false;
        if (typeof value === "number") return value === 0;
        if (typeof value === "string") return value.length === 0;
        if (Array.isArray(value)) return value.length === 0;
    }
    return false;
}

function writeEnumValue(writer: Utf8JsonWriter, type: Type, value: any, camelCase: boolean): void {
    const numericValue = Number(value);
    const name = getEnumName(type, numericValue);
    if (name != null) {
        const text = camelCase ? JsonNamingPolicy.CamelCase.ConvertName(name) : name;
        writer.WriteStringValue(text);
        return;
    }
    writer.WriteNumberValue(numericValue);
}

function hasStringEnumConverter(options?: JsonSerializerOptions): boolean {
    if (!options?.Converters) {
        return false;
    }
    for (let i = 0; i < options.Converters.count; i++) {
        const converter = options.Converters[i];
        if (converter instanceof JsonStringEnumConverter) {
            return true;
        }
    }
    return false;
}

function getEnumName(type: Type, value: number): string | null {
    const enumObj = (type as any)._ctor;
    if (!enumObj || typeof enumObj !== "object") {
    return null;
}
    const name = enumObj[value];
    return typeof name === "string" ? name : null;
}

function toBase64(bytes: Uint8Array): string {
    if (typeof Buffer !== "undefined" && typeof Buffer.from === "function") {
        return Buffer.from(bytes).toString("base64");
    }
    let binary = "";
    for (let i = 0; i < bytes.length; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    if (typeof btoa === "function") {
        return btoa(binary);
    }
    return binary;
}

function fromBase64(text: string): Uint8Array {
    if (typeof Buffer !== "undefined" && typeof Buffer.from === "function") {
        return new Uint8Array(Buffer.from(text, "base64"));
    }
    if (typeof atob !== "function") {
        return new Uint8Array(0);
    }
    const binary = atob(text);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
}

function serializeValue(writer: Utf8JsonWriter, value: any, type: Type | null, options?: JsonSerializerOptions): void {
    if (value == null) {
        writer.WriteNullValue();
        return;
    }

    const enumAsString = hasStringEnumConverter(options);

    if (type && type.IsEnum) {
        const numericValue = Number(value);
        if (enumAsString) {
            const name = getEnumName(type, numericValue);
            if (name != null) {
                writer.WriteStringValue(name);
                return;
            }
        }
        writer.WriteNumberValue(numericValue);
        return;
    }

    if (value instanceof Guid) {
        writer.WriteStringValue(value.toString());
        return;
    }

    if (value instanceof DateTime) {
        writer.WriteStringValue(value.ToString());
        return;
    }

    if (value instanceof Uint8Array) {
        writer.WriteStringValue(toBase64(value));
        return;
    }

    if (typeof value === "string") {
        writer.WriteStringValue(value);
        return;
    }

    if (typeof value === "boolean") {
        writer.WriteBooleanValue(value);
        return;
    }

    if (typeof value === "number") {
        if (!Number.isFinite(value) && (options?.NumberHandling & JsonNumberHandling.AllowNamedFloatingPointLiterals)) {
            writer.WriteStringValue(String(value));
            return;
        }
        writer.WriteNumberValue(value);
        return;
    }

    if (Array.isArray(value)) {
        writer.WriteStartArray();
        for (let i = 0; i < value.length; i++) {
            serializeValue(writer, value[i], null, options);
        }
        writer.WriteEndArray();
        return;
    }

    if (value instanceof Dictionary) {
        writer.WriteStartObject();
        value.forEach((key: any, entry: any) => {
            writer.WritePropertyName(String(key));
            serializeValue(writer, entry, null, options);
        });
        writer.WriteEndObject();
        return;
    }

    if (value != null && typeof value.GetEnumerator === "function" && typeof value !== "string") {
        writer.WriteStartArray();
        const iterator = value.GetEnumerator();
        try {
            while (iterator.MoveNext()) {
                serializeValue(writer, iterator.Current, null, options);
            }
        } finally {
            if (iterator != null && typeof iterator.dispose === "function") {
                iterator.dispose();
            }
        }
        writer.WriteEndArray();
        return;
    }

    const runtimeType = type ?? Type.of(value);
    if (runtimeType && typeof runtimeType.GetProperties === "function") {
        const props = runtimeType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        const fullName = runtimeType.FullName ?? runtimeType.fullName ?? "";
        if (props.length > 0 || fullName !== "System.Object") {
            writer.WriteStartObject();
            for (let i = 0; i < props.length; i++) {
                const prop = props[i];
                if (!prop.canRead) {
                    continue;
                }
                const propValue = prop.GetValue(value);
                const ignoreCondition = getJsonIgnoreCondition(prop) ?? options?.DefaultIgnoreCondition ?? JsonIgnoreCondition.Never;
                if (shouldIgnoreValue(propValue, ignoreCondition)) {
                    continue;
                }
                const nameOverride = getJsonPropertyName(prop);
                const name = nameOverride ?? applyNamingPolicy(prop.name, options);
                writer.WritePropertyName(name);
                const converterType = getJsonConverterTypeName(prop);
                if (converterType && converterType.indexOf("CamelCaseEnumJsonConverter") >= 0 && prop.propertyType?.IsEnum) {
                    writeEnumValue(writer, prop.propertyType, propValue, true);
                    continue;
                }
                serializeValue(writer, propValue, prop.propertyType, options);
            }
            writer.WriteEndObject();
            return;
        }
    }

    writer.WriteStartObject();
    for (const key of Object.keys(value)) {
        const entry = value[key];
        if (options?.DefaultIgnoreCondition === JsonIgnoreCondition.WhenWritingNull && entry == null) {
            continue;
        }
        writer.WritePropertyName(key);
        serializeValue(writer, entry, null, options);
    }
    writer.WriteEndObject();
}

function convertValueFromJson(value: any, type: Type | null, options?: JsonSerializerOptions): any {
    if (value == null || !type) {
        return value;
    }

    if (type.IsEnum) {
        if (typeof value === "string") {
            const enumObj = (type as any)._ctor;
            if (enumObj) {
                if (value in enumObj) {
                    return enumObj[value];
                }
                const lowered = value.toLowerCase();
                for (const key of Object.keys(enumObj)) {
                    if (!isNaN(Number(key))) {
                        continue;
                    }
                    if (key.toLowerCase() === lowered) {
                        return enumObj[key];
                    }
                }
            }
        }
        return Number(value);
    }

    const fullName = type.FullName ?? type.fullName ?? "";

    if (fullName === "System.String") {
        return value != null ? String(value) : null;
    }

    if (fullName === "System.Boolean") {
        return !!value;
    }

    if (fullName === "System.Guid" && typeof value === "string") {
        return Guid.parse(value);
    }

    if (fullName === "System.DateTime" && typeof value === "string") {
        return DateTime.Parse(value);
    }

    if (fullName.endsWith("Byte[]")) {
        if (typeof value === "string") {
            return fromBase64(value);
        }
        if (Array.isArray(value)) {
            return new Uint8Array(value);
        }
    }

    if (Array.isArray(value)) {
        if (fullName.indexOf("List") >= 0) {
            const list = new List<any>();
            const elementTypeName = getGenericArgument(fullName, 0);
            const elementType = elementTypeName ? Type.get(elementTypeName) ?? null : null;
            if (elementType) {
                for (let i = 0; i < value.length; i++) {
                    list.add(convertValueFromJson(value[i], elementType, options));
                }
            } else {
                list.addRange(value);
            }
            return list;
        }
        return value;
    }

    if (value && typeof value === "object") {
        if (fullName.indexOf("Dictionary") >= 0) {
            const dict = new Dictionary<string, any>();
            const valueTypeName = getGenericArgument(fullName, 1);
            const valueType = valueTypeName ? Type.get(valueTypeName) ?? null : null;
            for (const key of Object.keys(value)) {
                const entry = value[key];
                dict.set(key, valueType ? convertValueFromJson(entry, valueType, options) : entry);
            }
            return dict;
        }

        const instance = (type as any)._ctor ? new (type as any)._ctor() : {};
        const props = typeof type.GetProperties === "function"
            ? type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            : [];
        if (props.length === 0) {
            for (const key of Object.keys(value)) {
                instance[key] = value[key];
            }
            return instance;
        }

        for (let i = 0; i < props.length; i++) {
            const prop = props[i];
            if (!prop.canWrite) {
                continue;
            }
            const nameOverride = getJsonPropertyName(prop);
            const defaultName = applyNamingPolicy(prop.name, options);
            const candidates = [nameOverride, defaultName, prop.name].filter((x) => !!x) as string[];
            let sourceKey: string | null = null;
            if (options?.PropertyNameCaseInsensitive) {
                const keyMap: Record<string, string> = {};
                for (const key of Object.keys(value)) {
                    keyMap[key.toLowerCase()] = key;
                }
                for (const candidate of candidates) {
                    const resolved = keyMap[candidate.toLowerCase()];
                    if (resolved) {
                        sourceKey = resolved;
                        break;
                    }
                }
            } else {
                for (const candidate of candidates) {
                    if (candidate in value) {
                        sourceKey = candidate;
                        break;
                    }
                }
            }
            if (!sourceKey) {
                continue;
            }
            const converted = convertValueFromJson(value[sourceKey], prop.propertyType, options);
            prop.SetValue(instance, converted);
        }

        return instance;
    }

    return value;
}

function getGenericArgument(typeName: string, index: number): string | null {
    if (!typeName) {
        return null;
    }
    const start = typeName.indexOf("<");
    const end = typeName.lastIndexOf(">");
    if (start < 0 || end <= start) {
        return null;
    }
    const inner = typeName.substring(start + 1, end);
    const args: string[] = [];
    let depth = 0;
    let current = "";
    for (let i = 0; i < inner.length; i++) {
        const ch = inner[i];
        if (ch === "<") {
            depth++;
            current += ch;
            continue;
        }
        if (ch === ">") {
            depth--;
            current += ch;
            continue;
        }
        if (ch === "," && depth === 0) {
            args.push(current.trim());
            current = "";
            continue;
        }
        current += ch;
    }
    if (current.trim().length > 0) {
        args.push(current.trim());
    }
    return index >= 0 && index < args.length ? args[index] : null;
}

export class JsonSerializer {
    public static get JsonSerializer(): typeof JsonSerializer {
        return JsonSerializer;
    }

    public static Serialize<T>(value: T, options?: JsonSerializerOptions): string;
    public static Serialize(writer: Utf8JsonWriter, value: any, inputType?: Type, options?: JsonSerializerOptions): void;
    public static Serialize(...args: any[]): any {
        if (args[0] instanceof Utf8JsonWriter) {
            const writer = args[0] as Utf8JsonWriter;
            const value = args[1];
            const type = args[2] instanceof Type ? (args[2] as Type) : null;
            const options = args[2] instanceof Type ? (args[3] as JsonSerializerOptions) : (args[2] as JsonSerializerOptions);
            serializeValue(writer, value, type, options);
            return;
        }

        const value = args[0];
        const options = args[1] as JsonSerializerOptions | undefined;
        const writerOptions = options ? Object.assign(new JsonWriterOptions(), { Indented: options.WriteIndented }) : undefined;
        const writer = new Utf8JsonWriter(undefined, writerOptions);
        const runtimeType = value != null ? Type.of(value) : null;
        serializeValue(writer, value, runtimeType, options);
        return writer.toString();
    }

    public static SerializeToUtf8Bytes<T>(value: T, options?: JsonSerializerOptions): Uint8Array {
        const json = JsonSerializer.Serialize(value, options);
        return Encoding.UTF8.getBytes(json);
    }

    public static Deserialize<T>(json: string, options?: JsonSerializerOptions): T;
    public static Deserialize(json: string, returnType: Type, options?: JsonSerializerOptions): any;
    public static Deserialize(...args: any[]): any {
        const json = args[0] ?? "";
        let returnType: Type | null = null;
        let options: JsonSerializerOptions | undefined;

        if (args[1] instanceof Type) {
            returnType = args[1] as Type;
            options = args[2] as JsonSerializerOptions;
        } else {
            options = args[1] as JsonSerializerOptions;
        }

        const text = options?.AllowTrailingCommas ? stripTrailingCommas(json) : json;
        const parsed = text && text.length > 0 ? JSON.parse(text) : null;

        if (returnType) {
            return convertValueFromJson(parsed, returnType, options);
        }

        return parsed;
    }
}

export class JsonException extends Error {
    constructor(message?: string) {
        super(message ?? "JsonException");
        this.name = "JsonException";
    }

    public static New3(message: string): JsonException {
        return new JsonException(message);
    }
}

declare global {
    var JsonException: typeof JsonException;
}

if (typeof globalThis !== "undefined" && !(globalThis as any).JsonException) {
    (globalThis as any).JsonException = JsonException;
}
