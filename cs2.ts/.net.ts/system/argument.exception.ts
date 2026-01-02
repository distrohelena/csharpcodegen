// @ts-nocheck
export class ArgumentException extends Error {
    public ParamName?: string;
    public InnerException?: Error;

    constructor(message: string = "Value does not fall within the expected range.", paramNameOrInnerException?: string | Error | null, innerException?: Error | null) {
        let paramName: string | undefined;
        let inner: Error | undefined;

        if (typeof paramNameOrInnerException === "string") {
            paramName = paramNameOrInnerException;
            inner = innerException ?? undefined;
        } else if (paramNameOrInnerException != null) {
            inner = paramNameOrInnerException;
        }

        const fullMessage = paramName ? `${message} (Parameter '${paramName}')` : message;
        super(fullMessage);
        this.name = "ArgumentException";
        this.ParamName = paramName;
        this.InnerException = inner;
        Object.setPrototypeOf(this, new.target.prototype); // Restore prototype chain
    }
}
