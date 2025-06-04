export class NotImplementedException extends Error {
    constructor(message: string = "Operation is not implemented.") {
        super(message);
        this.name = "NotImplementedException";
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
