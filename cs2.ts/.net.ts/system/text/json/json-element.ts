// @ts-nocheck
import { JsonValueKind } from "./json-value-kind";
import { JsonProperty } from "./json-property";

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

    public GetString(): string {
        if (this._value == null) {
            return null;
        }
        return typeof this._value === "string" ? this._value : String(this._value);
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

    public EnumerateArray(): JsonArrayEnumerator {
        const items = Array.isArray(this._value) ? this._value : [];
        return new JsonArrayEnumerator(items);
    }

    public EnumerateObject(): JsonObjectEnumerator {
        const obj = this._value && typeof this._value === "object" && !Array.isArray(this._value) ? this._value : {};
        return new JsonObjectEnumerator(obj);
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
}
