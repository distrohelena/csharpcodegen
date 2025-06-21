export class InvalidOperationException extends Error {
    constructor(message: string = "Operation is not supported.") {
        super(message);
        this.name = "InvalidOperationException";
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
