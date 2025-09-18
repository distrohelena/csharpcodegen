// @ts-nocheck
﻿export class NotSupportedException extends Error {
    constructor(message: string = "Operation is not supported.") {
        super(message);
        this.name = "NotSupportedException";
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
