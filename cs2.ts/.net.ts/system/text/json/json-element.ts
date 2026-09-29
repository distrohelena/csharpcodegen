// @ts-nocheck
import { JsonValueKind } from "./json-value-kind";
import { JsonProperty } from "./json-property";
import { ArgumentNullException } from "../../argument-null.exception";
import { InvalidOperationException } from "../../invalid-operation.exception";
import { KeyNotFoundException } from "../../collections/generic/key-not-found.exception";

export class JsonElement {
    private _value: any;

    constructor(value: any) {
        this._value = value;
    }

    public get ValueKind(): JsonValueKind {
        const value = this._value;
        if (value === null) {
            return JsonValueKind.Null;
        }
        if (value === undefined) {
            return JsonValueKind.Undefined;
        }
        if (Array.isArray(value)) {
            return JsonValueKind.Array;
        }
        switch (typeof value) {
            case "string":
                return JsonValueKind.String;
            case "number":
                return JsonValueKind.Number;
            case "boolean":
                return value ? JsonValueKind.True : JsonValueKind.False;
            case "object":
                return JsonValueKind.Object;
            default:
                return JsonValueKind.Undefined;
        }
    }

    public GetRawText(): string {
        const text = JSON.stringify(this._value);
        return text == null ? "null" : text;
    }

    /** Returns a JSON string or null; numbers, booleans and containers are not coerced. */
    public GetString(): string {
        if (this.ValueKind === JsonValueKind.Null) {
            return null;
        }
        this.RequireKind(JsonValueKind.String);
        return this._value;
    }

    /** Reads an actual JSON Boolean, rejecting text and numeric substitutes. */
    public GetBoolean(): boolean {
        if (this.ValueKind !== JsonValueKind.True && this.ValueKind !== JsonValueKind.False) {
            throw new InvalidOperationException("The requested operation requires a JSON Boolean.");
        }
        return this._value;
    }

    /** Returns the number of entries in a JSON array, including null values. */
    public GetArrayLength(): number {
        this.RequireKind(JsonValueKind.Array);
        return this._value.length;
    }

    /** Rejects a mismatched JSON kind instead of substituting an empty container or scalar. */
    private RequireKind(kind: JsonValueKind): void {
        if (this.ValueKind !== kind) {
            throw new InvalidOperationException("The JSON element has an incompatible value kind.");
        }
    }

    public GetDecimal(): number {
        if (typeof this._value === "number") {
            return this._value;
        }
        return Number(this._value);
    }

    public TryGetInt64(outValue: { value: number }): boolean {
        if (typeof this._value === "number" && Number.isFinite(this._value) && Math.floor(this._value) === this._value) {
            outValue.value = this._value;
            return true;
        }
        outValue.value = 0;
        return false;
    }

    public TryGetDouble(outValue: { value: number }): boolean {
        if (typeof this._value === "number" && Number.isFinite(this._value)) {
            outValue.value = this._value;
            return true;
        }
        outValue.value = 0;
        return false;
    }

    /** Looks up an exact own-property name; an absent property returns an undefined JsonElement. */
    public TryGetProperty(name: string, outValue: { value: JsonElement }): boolean {
        if (name == null) {
            throw new ArgumentNullException("propertyName");
        }
        this.RequireKind(JsonValueKind.Object);
        if (Object.prototype.hasOwnProperty.call(this._value, name)) {
            const value = this._value[name];
            outValue.value = value instanceof JsonElement ? value : new JsonElement(value);
            return true;
        }
        outValue.value = new JsonElement(undefined);
        return false;
    }

    /** Requires an exact object property, throwing for absent names without consulting the prototype. */
    public GetProperty(name: string): JsonElement {
        const result: { value: JsonElement } = { value: undefined };
        if (!this.TryGetProperty(name, result)) {
            throw new KeyNotFoundException();
        }
        return result.value;
    }

    /** Enumerates only an actual JSON array; nonarrays are rejected before iteration starts. */
    public EnumerateArray(): JsonArrayEnumerator {
        this.RequireKind(JsonValueKind.Array);
        return new JsonArrayEnumerator(this._value);
    }

    /** Enumerates only own JSON object properties, rejecting null, arrays and scalar values. */
    public EnumerateObject(): JsonObjectEnumerator {
        this.RequireKind(JsonValueKind.Object);
        return new JsonObjectEnumerator(this._value);
    }

}

class JsonArrayEnumerator {
    private _items: any[];
    private _index: number = -1;

    constructor(items: any[]) {
        this._items = items;
    }

    public MoveNext(): boolean {
        this._index++;
        return this._index < this._items.length;
    }

    public get Current(): JsonElement {
        return new JsonElement(this._items[this._index]);
    }

    public next(): IteratorResult<JsonElement> {
        if (this.MoveNext()) {
            return { value: this.Current, done: false };
        }
        return { value: undefined as any, done: true };
    }

    public [Symbol.iterator](): Iterator<JsonElement> {
        return this;
    }
}

class JsonObjectEnumerator {
    private _entries: Array<[string, any]>;
    private _index: number = -1;

    constructor(obj: Record<string, any>) {
        this._entries = Object.entries(obj);
    }

    public MoveNext(): boolean {
        this._index++;
        return this._index < this._entries.length;
    }

    public get Current(): JsonProperty {
        const entry = this._entries[this._index];
        return new JsonProperty(entry[0], entry[1]);
    }

    public next(): IteratorResult<JsonProperty> {
        if (this.MoveNext()) {
            return { value: this.Current, done: false };
        }
        return { value: undefined as any, done: true };
    }

    public [Symbol.iterator](): Iterator<JsonProperty> {
        return this;
    }
}
