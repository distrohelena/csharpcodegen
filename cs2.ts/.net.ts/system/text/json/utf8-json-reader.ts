// @ts-nocheck
export class Utf8JsonReader {
    private _json: string;

    constructor(data: string | Uint8Array) {
        if (data instanceof Uint8Array) {
            this._json = new TextDecoder("utf-8").decode(data);
        } else {
            this._json = data ?? "";
        }
    }

    public getRawText(): string {
        return this._json;
    }
}
