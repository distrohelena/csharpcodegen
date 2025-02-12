export class ArgumentException extends Error {
    constructor(message: string = "Operation is not supported.") {
        super(message);
        this.name = "ArgumentException";
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
