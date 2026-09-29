/** Controls whether JSON comments are rejected, skipped, or surfaced by a reader. */
export enum JsonCommentHandling {
    /** Reject comments in JSON input. */
    Disallow = 0,
    /** Skip comments while preserving the surrounding JSON tokens. */
    Skip = 1,
    /** Reader-only mode; JsonDocumentOptions rejects this value. */
    Allow = 2,
}
