import { ArgumentNullException } from "../argument-null.exception";
import { ArgumentException } from "../argument.exception";

export class NativeStringUtil {
    static readonly Empty: string = "";

    static isNullOrEmpty(value?: string | null): boolean {
        return !value || value.length === 0;
    }

    static isNullOrWhiteSpace(value?: string | null): boolean {
        return value == null || value.trim().length === 0;
    }

    static isLetter(value?: string | null): boolean {
        if (!value || value.length === 0) {
            return false;
        }
        const code = value.charCodeAt(0);
        return (code >= 65 && code <= 90) || (code >= 97 && code <= 122);
    }

    static isLetterOrDigit(value?: string | null): boolean {
        if (!value || value.length === 0) {
            return false;
        }
        const code = value.charCodeAt(0);
        return (code >= 48 && code <= 57) ||
            (code >= 65 && code <= 90) ||
            (code >= 97 && code <= 122);
    }

    /** Mirrors Enumerable.Any<char> over a string using UTF-16 code units, the .NET char representation. */
    static any(value: string | null | undefined, predicate: ((character: string) => boolean) | null | undefined): boolean {
        if (value === null || value === undefined) {
            throw new ArgumentNullException("source");
        }
        if (predicate === null || predicate === undefined) {
            throw new ArgumentNullException("predicate");
        }
        for (let index = 0; index < value.length; index++) {
            if (predicate(value.charAt(index))) {
                return true;
            }
        }
        return false;
    }
    /** Mirrors Enumerable.All<char> over a string using UTF-16 code units, the .NET char representation. */
    static all(value: string | null | undefined, predicate: ((character: string) => boolean) | null | undefined): boolean {
        if (value === null || value === undefined) {
            throw new ArgumentNullException("source");
        }
        if (predicate === null || predicate === undefined) {
            throw new ArgumentNullException("predicate");
        }
        for (let index = 0; index < value.length; index++) {
            if (!predicate(value.charAt(index))) {
                return false;
            }
        }
        return true;
    }
    /** Mirrors String.Split(separator, StringSplitOptions) for the browser-emitted overload. */
    static split(value: string | null | undefined, separator: string | null | undefined, options: number): string[] {
        if (value === null || value === undefined) {
            throw new ArgumentNullException("source");
        }
        if (separator === null || separator === undefined) {
            throw new ArgumentNullException("separator");
        }
        let parts = value.split(separator);
        if ((options & 2) !== 0) {
            parts = parts.map(part => part.trim());
        }
        return (options & 1) !== 0 ? parts.filter(part => part.length > 0) : parts;
    }
    /** Mirrors String.Join for separator plus IEnumerable or params values. */
    static join(separator: string | null | undefined, ...values: unknown[]): string {
        const actualSeparator = separator ?? "";
        if (values.length === 1 && values[0] === null) {
            throw new ArgumentNullException("value");
        }
        let items: unknown[];
        if (values.length === 1 && typeof values[0] !== "string" && values[0] != null &&
            typeof (values[0] as { [Symbol.iterator]?: unknown })[Symbol.iterator] === "function") {
            items = Array.from(values[0] as Iterable<unknown>);
        } else {
            items = values;
        }
        return items.map(value => value == null ? "" : String(value)).join(actualSeparator);
    }
    /** Mirrors Enumerable.Select<char, TResult> over a string using UTF-16 code units. */
    static select<TResult>(value: string | null | undefined, selector: ((character: string, index: number) => TResult) | null | undefined): TResult[] {
        if (value === null || value === undefined) {
            throw new ArgumentNullException("source");
        }
        if (selector === null || selector === undefined) {
            throw new ArgumentNullException("selector");
        }
        const result: TResult[] = new Array<TResult>(value.length);
        for (let index = 0; index < value.length; index++) {
            result[index] = selector(value.charAt(index), index);
        }
        return result;
    }
    /** Mirrors String.Replace overloads with literal, all-occurrence replacement. */
    static replace(value: string | null | undefined, oldValue: string | null | undefined, newValue: string | null | undefined): string {
        if (value === null || value === undefined) {
            throw new ArgumentNullException("value");
        }
        if (oldValue === null || oldValue === undefined) {
            throw new ArgumentNullException("oldValue");
        }
        if (oldValue.length === 0) {
            throw new ArgumentException("String cannot be of zero length.", "oldValue");
        }
        return value.split(oldValue).join(newValue ?? "");
    }
    /** Mirrors String.Trim/TrimStart/TrimEnd(params char[]) over UTF-16 code units. */
    static trimCharacters(
        value: string | null | undefined,
        mode: "both" | "start" | "end",
        ...trimCharacters: Array<string | Iterable<string> | null | undefined>
    ): string {
        if (value === null || value === undefined) {
            throw new ArgumentNullException("value");
        }
        const characters = new Set<string>();
        for (const candidate of trimCharacters) {
            if (candidate === null || candidate === undefined) {
                continue;
            }
            if (typeof candidate === "string") {
                for (let index = 0; index < candidate.length; index++) {
                    characters.add(candidate.charAt(index));
                }
                continue;
            }
            for (const character of candidate) {
                const text = String(character);
                if (text.length > 0) {
                    characters.add(text.charAt(0));
                }
            }
        }
        if (characters.size === 0) {
            return mode === "start" ? value.trimStart() : mode === "end" ? value.trimEnd() : value.trim();
        }

        let start = 0;
        let end = value.length - 1;
        if (mode !== "end") {
            while (start <= end && characters.has(value.charAt(start))) {
                start++;
            }
        }
        if (mode !== "start") {
            while (end >= start && characters.has(value.charAt(end))) {
                end--;
            }
        }
        return value.substring(start, end + 1);
    }
    static toCamelCase(value?: string | null): string {
        if (!value || value.length === 0) {
            return value ?? "";
        }
        if (value.length === 1) {
            return value.toLowerCase();
        }
        return value[0].toLowerCase() + value.substring(1);
    }
}

declare global {
    interface StringConstructor {
        IsAsciiLetterOrDigit(value: string): boolean;
    }
}

if (typeof String.IsAsciiLetterOrDigit !== "function") {
    String.IsAsciiLetterOrDigit = (value: string): boolean => NativeStringUtil.isLetterOrDigit(value);
}
