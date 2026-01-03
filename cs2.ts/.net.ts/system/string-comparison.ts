// @ts-nocheck
export enum StringComparison {
    CurrentCulture = 0,
    CurrentCultureIgnoreCase = 1,
    InvariantCulture = 2,
    InvariantCultureIgnoreCase = 3,
    Ordinal = 4,
    OrdinalIgnoreCase = 5
}

declare global {
    interface StringConstructor {
        Equals(a: string, b: string, comparison?: StringComparison): boolean;
        CompareOrdinal(a: string, b: string): number;
    }
}

if (!(String as any).Equals) {
    (String as any).Equals = function (a: string, b: string, comparison: StringComparison = StringComparison.Ordinal) {
        if (a == null || b == null) {
            return a === b;
        }

        switch (comparison) {
            case StringComparison.OrdinalIgnoreCase:
            case StringComparison.CurrentCultureIgnoreCase:
            case StringComparison.InvariantCultureIgnoreCase:
                return a.toLowerCase() === b.toLowerCase();
            default:
                return a === b;
        }
    };
}

if (!(String as any).CompareOrdinal) {
    (String as any).CompareOrdinal = function (a: string, b: string) {
        if (a === b) {
            return 0;
        }
        if (a == null) {
            return -1;
        }
        if (b == null) {
            return 1;
        }
        if (a < b) {
            return -1;
        }
        if (a > b) {
            return 1;
        }
        return 0;
    };
}
