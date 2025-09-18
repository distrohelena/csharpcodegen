// @ts-nocheck
export class CryptographicException extends Error {
    constructor(message: string = "Cryptographic exception.") {
        super(message);
        this.name = "CryptographicException";
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
