// @ts-nocheck
import { JsonDocumentOptions } from "./json-document-options";
import { JsonElementSource } from "./json-element-source";
import { ArgumentNullException } from "../../argument-null.exception";
import { JsonElement } from "./json-element";
import { Utf8JsonReader } from "./utf8-json-reader";

import { JsonCommentHandling } from "./json-comment-handling";
import { JsonTextParser } from "./json-text-parser";

export class JsonDocument {
    private _root: JsonElement;

    private constructor(value: any, source: JsonElementSource) {
        this._root = new JsonElement(value, source);
    }

    public static Parse(json: string | Uint8Array, options?: JsonDocumentOptions): JsonDocument {
        if (json == null) throw new ArgumentNullException("json");
        const text = typeof json === "string" ? json : new TextDecoder("utf-8", { fatal: true }).decode(json);
        const value = JsonTextParser.Parse(text, options?.CommentHandling === JsonCommentHandling.Skip, options?.AllowTrailingCommas === true);
        return new JsonDocument(value, JsonElementSource.ParseValidated(text));
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
