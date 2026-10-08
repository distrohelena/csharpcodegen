// @ts-nocheck
import { ArgumentNullException } from '../../../argument-null.exception';
import { KeyValuePair } from '../../../collections/generic/key-value-pair';

/** Bounded System.Text.Json.Nodes support for parsed JSON trees used by browser-side authorization. */
export abstract class JsonNode {
    /** Parses JSON text into mutable node wrappers; JSON null maps to a null node reference. */
    public static Parse(json: string, _nodeOptions: unknown = null, _documentOptions: unknown = null): JsonNode | null {
        if (json === null || json === undefined) throw new ArgumentNullException('json');
        return JsonNode.FromValue(JSON.parse(json));
    }

    /** Converts JSON-compatible values to their corresponding node wrapper without coercion. */
    public static FromValue(value: unknown): JsonNode | null {
        if (value === null || value === undefined) return null;
        if (value instanceof JsonNode) return value;
        if (Array.isArray(value)) return new JsonArray(value);
        if (typeof value === 'object') return new JsonObject(value as Record<string, unknown>);
        return new JsonValue(value);
    }

    /** Serializes the current node tree using JSON's compact syntax. */
    public ToJsonString(_options: unknown = null): string { return JSON.stringify(this.toJSON()); }
    public abstract toJSON(): unknown;
}

/** Scalar JSON node. The current converted callers only request numeric values. */
export class JsonValue extends JsonNode {
    private readonly __value: unknown;
    constructor(value: unknown) { super(); this.__value = value; }

    public TryGetValue<T>(outValue: { value?: T } | null | undefined): boolean {
        if (outValue === null || outValue === undefined) throw new ArgumentNullException('value');
        if (typeof this.__value === 'number' && Number.isFinite(this.__value)) { outValue.value = this.__value as T; return true; }
        outValue.value = null;
        return false;
    }

    public toJSON(): unknown { return this.__value; }
}

/** Mutable JSON array with .NET-style Count while preserving JavaScript index writes and iteration. */
export class JsonArray extends Array<JsonNode | null> {
    constructor(values: readonly unknown[] = []) {
        super();
        Object.setPrototypeOf(this, JsonArray.prototype);
        for (const value of values) this.push(JsonNode.FromValue(value));
    }

    public get Count(): number { return this.length; }
    public ToJsonString(_options: unknown = null): string { return JSON.stringify(this.toJSON()); }
    public toJSON(): unknown[] { return Array.from(this, value => value === null ? null : value.toJSON()); }
}

/** Mutable JSON object with .NET-style enumeration and index access for converted JsonObject callers. */
export class JsonObject extends JsonNode implements Iterable<KeyValuePair<string, JsonNode | null>> {
    private readonly __entries = new Map<string, JsonNode | null>();

    constructor(values: Record<string, unknown> = {}) {
        super();
        for (const [key, value] of Object.entries(values)) this.__entries.set(key, JsonNode.FromValue(value));
        return new Proxy(this, {
            get: (target, property, receiver) => {
                if (typeof property === 'string' && !property.startsWith('__') && !Reflect.has(target, property)) return target.__entries.has(property) ? target.__entries.get(property) : null;
                return Reflect.get(target, property, receiver);
            },
            set: (target, property, value, receiver) => {
                if (typeof property === 'string' && !property.startsWith('__') && !Reflect.has(target, property)) { target.__entries.set(property, JsonNode.FromValue(value)); return true; }
                return Reflect.set(target, property, value, receiver);
            }
        }) as unknown as JsonObject;
    }

    public get Count(): number { return this.__entries.size; }

    public TryGetPropertyValue(name: string, outValue: { value?: JsonNode | null } | null | undefined): boolean {
        if (name === null || name === undefined) throw new ArgumentNullException('propertyName');
        if (outValue === null || outValue === undefined) throw new ArgumentNullException('jsonNode');
        if (!this.__entries.has(name)) { outValue.value = null; return false; }
        outValue.value = this.__entries.get(name) ?? null;
        return true;
    }

    public *[Symbol.iterator](): Iterator<KeyValuePair<string, JsonNode | null>> {
        for (const [key, value] of this.__entries) yield new KeyValuePair(key, value);
    }

    public toJSON(): Record<string, unknown> {
        const result: Record<string, unknown> = {};
        for (const [key, value] of this.__entries) result[key] = value === null ? null : value.toJSON();
        return result;
    }
}
