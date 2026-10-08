/** Typed singleton representing a database null rather than a JavaScript object or missing value. */
export class DBNull {
    /** Shared database-null identity used by generated reference and type checks. */
    static readonly Value: DBNull = new DBNull();

    /** Prevents constructing additional database-null identities from typed callers. */
    private constructor() {}

    /** Database null formats as an empty string, independently of the optional format provider. */
    ToString(_provider?: unknown): string { return ''; }

    /** Preserves .NET formatting when JavaScript performs ordinary string coercion. */
    toString(): string { return this.ToString(); }
}