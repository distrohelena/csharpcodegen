/** Signals that a required key is absent from a collection or JSON object. */
export class KeyNotFoundException extends Error {
    /** Preserves the standard .NET missing-key HRESULT. */
    public readonly HResult: number = -2146232969;
    /** Retains the originating failure independently from the public message. */
    public readonly InnerException: unknown;

    /** Creates a native error with the .NET default message when no message is supplied. */
    public constructor(message: string = 'The given key was not present in the dictionary.', innerException: unknown = null) {
        super(message === null ? 'The given key was not present in the dictionary.' : message);
        this.name = 'KeyNotFoundException';
        this.InnerException = innerException;
        Object.setPrototypeOf(this, new.target.prototype);
    }

    /** Provides the System.Exception property expected by generated consumers. */
    public get Message(): string { return this.message; }
}
