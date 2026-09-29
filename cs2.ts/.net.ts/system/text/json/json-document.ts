// @ts-nocheck
import { JsonDocumentOptions } from "./json-document-options";
import { JsonElement } from "./json-element";
import { Utf8JsonReader } from "./utf8-json-reader";

import { JsonCommentHandling } from "./json-comment-handling";
import { JsonTextParser } from "./json-text-parser";

export class JsonDocument {
    private _root: JsonElement;

    private constructor(value: any) {
        this._root = new JsonElement(value);
    }

    public static Parse(json: string, options?: JsonDocumentOptions): JsonDocument {
        const value = JsonTextParser.Parse(json, options?.CommentHandling === JsonCommentHandling.Skip, options?.AllowTrailingCommas === true);
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
