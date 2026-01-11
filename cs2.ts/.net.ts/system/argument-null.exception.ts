// @ts-nocheck
import { ArgumentException } from "./argument.exception";

export class ArgumentNullException extends ArgumentException {
    constructor();
    constructor(paramName: string);
    constructor(paramName: string, message: string);
    constructor(paramName?: string | null, message?: string | null) {
        const finalMessage = message ?? "Value cannot be null.";
        const finalParamName = paramName ?? undefined;

        super(finalMessage, finalParamName);
        this.name = "ArgumentNullException";
        Object.setPrototypeOf(this, new.target.prototype);
    }
}
