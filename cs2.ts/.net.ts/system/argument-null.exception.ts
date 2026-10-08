// @ts-nocheck
import { ArgumentException } from "./argument.exception";

export class ArgumentNullException extends ArgumentException {
    public static ThrowIfNull(value: any, paramName?: string | null): void {
        if (value === null || value === undefined) {
            throw new ArgumentNullException(paramName ?? undefined);
        }
    }

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
