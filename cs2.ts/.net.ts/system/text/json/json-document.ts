// @ts-nocheck
import { JsonDocumentOptions } from "./json-document-options";
import { JsonElement } from "./json-element";
import { Utf8JsonReader } from "./utf8-json-reader";

function stripTrailingCommas(text: string): string {
    return text.replace(/,\s*([}\]])/g, "$1");
}

export class JsonDocument {
    private _root: JsonElement;

    private constructor(value: any) {
        this._root = new JsonElement(value);
    }

    public static Parse(json: string, options?: JsonDocumentOptions): JsonDocument {
        const text = options?.AllowTrailingCommas ? stripTrailingCommas(json) : json;
        const value = text && text.length > 0 ? JSON.parse(text) : null;
        return new JsonDocument(value);
    }

    public static ParseValue(reader: Utf8JsonReader): JsonDocument {
        return JsonDocument.Parse(reader.getRawText());
    }

    public get RootElement(): JsonElement {
        return this._root;
    }

    public dispose(): void {
    }
}
