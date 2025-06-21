export class ArgumentNullException extends Error {
    constructor(message: string = "Argument is null.") {
        super(message);
        this.name = "ArgumentNullException";
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
