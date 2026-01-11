// @ts-nocheck
function isUpper(value: string): boolean {
    return value === value.toUpperCase() && value !== value.toLowerCase();
}

function convertToCamelCase(name: string): string {
    if (!name) {
        return name;
    }
    if (!isUpper(name[0])) {
        return name;
    }

    const chars = name.split("");
    for (let i = 0; i < chars.length; i++) {
        if (i === 1 && !isUpper(chars[i])) {
            break;
        }
        const hasNext = i + 1 < chars.length;
        if (i > 0 && hasNext && !isUpper(chars[i + 1])) {
            break;
        }
        chars[i] = chars[i].toLowerCase();
    }
    return chars.join("");
}

export class JsonNamingPolicy {
    private readonly converter: (name: string) => string;

    protected constructor(converter?: (name: string) => string) {
        this.converter = converter ?? ((value) => value);
    }

    public ConvertName(name: string): string {
        if (name == null) {
            return name as any;
        }
        return this.converter(name);
    }

    public static readonly CamelCase: JsonNamingPolicy = new JsonNamingPolicy(convertToCamelCase);
}
