/** Retains all failures from synchronous cancellation callbacks. */
export class AggregateException extends Error {
    /** Failures in the order they were observed. */
    public readonly InnerExceptions: readonly unknown[];
    /** Snapshots the callback failures without substituting their identities. */
    constructor(errors: readonly unknown[]) {
        super('One or more errors occurred.');
        this.name = 'AggregateException';
        this.InnerExceptions = Object.freeze([...errors]);
    }
    /** First failure, matching the aggregate's primary exception. */
    public get InnerException(): unknown { return this.InnerExceptions[0]; }
    /** Exposes the CLR message spelling. */
    public get Message(): string { return this.message; }
}
