// @ts-nocheck
export enum JsonTokenType {
    None = 0,
    StartObject = 1,
    EndObject = 2,
    StartArray = 3,
    EndArray = 4,
    PropertyName = 5,
    String = 6,
    Number = 7,
    True = 8,
    False = 9,
    Null = 10
}

export class Utf8JsonReader {
    private _json: string;
    private _tokenType: JsonTokenType = JsonTokenType.None;
    private _stringValue: string | null = null;
    private _numberValue: number | null = null;

    constructor(data: string | Uint8Array) {
        if (data instanceof Uint8Array) {
            this._json = new TextDecoder("utf-8").decode(data);
        } else {
            this._json = data ?? "";
        }
        this.parseToken();
    }

    public get TokenType(): JsonTokenType {
        return this._tokenType;
    }

    public GetString(): string | null {
        if (this._tokenType !== JsonTokenType.String) {
            return null;
        }
        return this._stringValue;
    }

    public TryGetInt32(outValue: { value: number }): boolean {
        if (this._tokenType !== JsonTokenType.Number || this._numberValue == null || Number.isNaN(this._numberValue)) {
            outValue.value = 0;
            return false;
        }
        const numeric = Math.trunc(this._numberValue);
        if (numeric < -2147483648 || numeric > 2147483647) {
            outValue.value = 0;
            return false;
        }
        outValue.value = numeric;
        return true;
    }

    public getRawText(): string {
        return this._json;
    }

    private parseToken(): void {
        const text = (this._json ?? "").trim();
        if (!text) {
            this._tokenType = JsonTokenType.None;
            return;
        }
        if (text.startsWith("\"")) {
            try {
                this._stringValue = JSON.parse(text);
                this._tokenType = JsonTokenType.String;
            } catch {
                this._stringValue = text.slice(1, -1);
                this._tokenType = JsonTokenType.String;
            }
            return;
        }
        if (text === "true") {
            this._tokenType = JsonTokenType.True;
            return;
        }
        if (text === "false") {
            this._tokenType = JsonTokenType.False;
            return;
        }
        if (text === "null") {
            this._tokenType = JsonTokenType.Null;
            return;
        }
        if (text.startsWith("{")) {
            this._tokenType = JsonTokenType.StartObject;
            return;
        }
        if (text.startsWith("[")) {
            this._tokenType = JsonTokenType.StartArray;
            return;
        }
        const parsed = Number(text);
        if (!Number.isNaN(parsed)) {
            this._numberValue = parsed;
            this._tokenType = JsonTokenType.Number;
            return;
        }
        this._tokenType = JsonTokenType.None;
    }
}

declare global {
    var JsonTokenType: typeof JsonTokenType;
}

if (typeof globalThis !== "undefined" && !(globalThis as any).JsonTokenType) {
    (globalThis as any).JsonTokenType = JsonTokenType;
}
