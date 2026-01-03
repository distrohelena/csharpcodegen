// @ts-nocheck
import { ArgumentException } from "./argument.exception";

export class ArgumentOutOfRangeException extends ArgumentException {
    public ActualValue?: any;

    constructor(paramName?: string, actualValueOrMessage?: any, message?: string) {
        let actualValue: any = undefined;
        let finalMessage = "Specified argument was out of the range of valid values.";
        let finalParamName: string | undefined = paramName ?? undefined;

        if (message !== undefined) {
            actualValue = actualValueOrMessage;
            finalMessage = message ?? finalMessage;
        } else if (typeof actualValueOrMessage === "string") {
            finalMessage = actualValueOrMessage;
        } else if (actualValueOrMessage !== undefined) {
            actualValue = actualValueOrMessage;
        }

        super(finalMessage, finalParamName);
        this.name = "ArgumentOutOfRangeException";
        this.ActualValue = actualValue;
        Object.setPrototypeOf(this, new.target.prototype);
    }
}
