/** Access to a resource after its owning object has released it. */
export class ObjectDisposedException extends Error {
    /** Identifies the disposed object in converted exception handling. */
    public readonly ObjectName: string;
    /** Creates an error for a disposed runtime object. */
    constructor(objectName: string) {
        super(`Cannot access a disposed object: ${objectName}.`);
        this.name = 'ObjectDisposedException';
        this.ObjectName = objectName;
    }
    /** Exposes the CLR message spelling. */
    public get Message(): string { return this.message; }
}
