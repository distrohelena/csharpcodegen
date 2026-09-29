/** Signals that the selected principal or enrolled device lacks the required authority. */
export class UnauthorizedAccessException extends Error {
    /** Preserves the .NET HRESULT for an access-denied exception. */
    public readonly HResult: number = -2147024891;
    /** Retains the originating failure without exposing it through the public message. */
    public readonly InnerException: unknown;

    /** Creates a native Error with the .NET unauthorized-operation message and optional inner failure. */
    public constructor(message: string = 'Attempted to perform an unauthorized operation.', innerException: unknown = null) {
        super(message === null ? 'Attempted to perform an unauthorized operation.' : message);
        this.name = 'UnauthorizedAccessException';
        this.InnerException = innerException;
        Object.setPrototypeOf(this, new.target.prototype);
    }

    /** Exposes the native error message through the property generated for System.Exception.Message. */
    public get Message(): string { return this.message; }
}
