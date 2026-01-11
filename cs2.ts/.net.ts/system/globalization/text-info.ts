// @ts-nocheck
export class TextInfo {
    private readonly cultureName: string;

    constructor(cultureName?: string) {
        this.cultureName = cultureName ?? "Invariant Culture";
    }

    public ToLower(value: string): string {
        if (value == null) {
            return value as any;
        }
        return value.toLowerCase();
    }

    public ToUpper(value: string): string {
        if (value == null) {
            return value as any;
        }
        return value.toUpperCase();
    }

    public ToTitleCase(value: string): string {
        if (value == null) {
            return value as any;
        }
        return value.replace(/\b([A-Za-z])([A-Za-z]*)/g, (_match, first, rest) => {
            return first.toUpperCase() + rest.toLowerCase();
        });
    }
}
