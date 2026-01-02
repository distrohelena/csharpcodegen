// @ts-nocheck
import { IEqualityComparer } from "./collections/generic/iequalitycomparer";

export class StringComparer implements IEqualityComparer<string> {
    private readonly ignoreCase: boolean;

    private constructor(ignoreCase: boolean) {
        this.ignoreCase = ignoreCase;
    }

    public Equals(x: string, y: string): boolean {
        if (x === y) {
            return true;
        }
        if (x == null || y == null) {
            return false;
        }
        if (this.ignoreCase) {
            return x.toLowerCase() === y.toLowerCase();
        }
        return x === y;
    }

    public GetHashCode(obj: string): number {
        if (obj == null) {
            return 0;
        }
        const text = this.ignoreCase ? obj.toLowerCase() : obj;
        let hash = 0;
        for (let i = 0; i < text.length; i++) {
            hash = ((hash << 5) - hash) + text.charCodeAt(i);
            hash |= 0;
        }
        return hash;
    }

    public static readonly Ordinal: StringComparer = new StringComparer(false);
    public static readonly OrdinalIgnoreCase: StringComparer = new StringComparer(true);
}
