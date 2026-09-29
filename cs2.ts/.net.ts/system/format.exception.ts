/** Reports input that does not conform to the format required by a runtime conversion. */
export class FormatException extends Error {
    /** Retains an explicitly supplied originating error, omitting it when no inner error was supplied. */
    public InnerException?: Error;

    /** Creates the standard .NET invalid-format message. */
    constructor();
    /** Preserves an explicit public message, including an empty string. */
    constructor(message: string);
    /** Associates the public message with the originating conversion failure. */
    constructor(message: string, innerException: Error);
    /** Implements the overloads while preserving native Error identity and optional inner-error behavior. */
    constructor(message?: string | null, innerException?: Error | null) {
        const finalMessage = message ?? "One of the identified items was in an invalid format.";
        super(finalMessage);
        this.name = "FormatException";
        this.InnerException = innerException ?? undefined;
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
