// @ts-nocheck
import type { Exception } from "./exception";
export class InvalidOperationException extends Error {
    /** Returns the native error text through the property emitted for System.Exception.Message. */
    public get Message(): string {
        return this.message;
    }

    public readonly InnerException?: Error | Exception;

    constructor(message: string = "Operation is not supported.", innerException?: Error | Exception | null) {
        super(message);
        this.name = "InvalidOperationException";
        this.InnerException = innerException ?? undefined;
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
