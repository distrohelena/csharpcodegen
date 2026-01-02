import { RegexOptions } from "./regex-options";

function buildFlags(options?: RegexOptions): string {
    let flags = "";
    if (options == null) {
        return flags;
    }

    if (options & RegexOptions.IgnoreCase) {
        flags += "i";
    }
    if (options & RegexOptions.Multiline) {
        flags += "m";
    }
    if (options & RegexOptions.Singleline) {
        flags += "s";
    }
    return flags;
}

function sanitizePattern(pattern: string): string {
    return pattern;
}

export class Regex {
    private readonly _regex: RegExp;

    constructor(pattern: string, options: RegexOptions = RegexOptions.None) {
        const flags = buildFlags(options);
        this._regex = new RegExp(sanitizePattern(pattern), flags);
    }

    public IsMatch(input: string): boolean {
        return this._regex.test(input);
    }

    public Match(input: string): RegExpMatchArray | null {
        return input.match(this._regex);
    }

    public static IsMatch(input: string, pattern: string, options: RegexOptions = RegexOptions.None): boolean {
        if (input == null) {
            return false;
        }
        const regex = new Regex(pattern, options);
        return regex.IsMatch(input);
    }

    public static Match(input: string, pattern: string, options: RegexOptions = RegexOptions.None): RegExpMatchArray | null {
        if (input == null) {
            return null;
        }
        const regex = new Regex(pattern, options);
        return regex.Match(input);
    }

    public static Replace(input: string, pattern: string, replacement: string | ((match: RegExpExecArray) => string), options: RegexOptions = RegexOptions.None): string {
        if (input == null) {
            return input as any;
        }
        const flags = buildFlags(options) + "g";
        const regex = new RegExp(sanitizePattern(pattern), flags);
        if (typeof replacement === "function") {
            return input.replace(regex, (...args) => replacement(args as any));
        }
        return input.replace(regex, replacement as string);
    }
}
