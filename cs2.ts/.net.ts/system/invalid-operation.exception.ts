// @ts-nocheck
export class InvalidOperationException extends Error {
    public readonly InnerException?: Error;

    constructor(message: string = "Operation is not supported.", innerException?: Error | null) {
        super(message);
        this.name = "InvalidOperationException";
        this.InnerException = innerException ?? undefined;
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
