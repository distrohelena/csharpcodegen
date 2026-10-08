import { ArgumentException } from "./argument.exception";
import { ArgumentNullException } from "./argument-null.exception";

/** Framework argument guards that must preserve exception identity without depending on module evaluation order. */
export class ArgumentGuard {
    /** Throws the matching .NET argument exception when a required string is null, empty, or whitespace. */
    public static throwIfNullOrWhiteSpace(value: string | null | undefined, paramName?: string | null): void {
        if (value === null || value === undefined) {
            throw new ArgumentNullException(paramName ?? undefined);
        }
        if (value.trim().length === 0) {
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", paramName ?? undefined);
        }
    }
}
