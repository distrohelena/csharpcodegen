// @ts-nocheck
import { ArgumentOutOfRangeException } from "../../argument-out-of-range.exception";
import { JsonCommentHandling } from "./json-comment-handling";

/** Configures the JSON document syntax accepted by the browser runtime. */
export class JsonDocumentOptions {
    /** Permits a single comma after the final array element or object property. */
    public AllowTrailingCommas: boolean = false;
    /** Stores the document-compatible comment mode. */
    private commentHandling: JsonCommentHandling = JsonCommentHandling.Disallow;

    /** Returns whether document parsing rejects or skips comments. */
    public get CommentHandling(): JsonCommentHandling { return this.commentHandling; }

    /** Rejects reader-only and unknown modes rather than silently ignoring them. */
    public set CommentHandling(value: JsonCommentHandling) {
        if (value !== JsonCommentHandling.Disallow && value !== JsonCommentHandling.Skip) {
            throw new ArgumentOutOfRangeException('value');
        }
        this.commentHandling = value;
    }
}
