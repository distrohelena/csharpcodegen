/** Exact source slices for already validated JSON; no numeric conversion occurs in this metadata tree. */
export class JsonElementSource {
    /** Original JSON token, including container spacing and number spelling. */
    readonly Raw: string;
    /** Last occurrence of each object property, or the decimal index of each array item. */
    readonly Children: Map<string, JsonElementSource>;

    /** Stores immutable token text with the child lookup built during the scan. */
    private constructor(raw: string, children: Map<string, JsonElementSource>) {
        this.Raw = raw;
        this.Children = children;
    }

    /** Scans text only after JsonTextParser has validated its syntax and configured extensions. */
    static ParseValidated(text: string): JsonElementSource {
        return this.Read(text, { Index: 0 });
    }

    /** Reads a token and recursively records children without interpreting numeric values. */
    private static Read(text: string, cursor: { Index: number }): JsonElementSource {
        this.SkipIgnored(text, cursor);
        const start = cursor.Index;
        const children = new Map<string, JsonElementSource>();
        const opening = text[cursor.Index];
        if (opening === '{' || opening === '[') {
            cursor.Index++;
            const closing = opening === '{' ? '}' : ']';
            let index = 0;
            this.SkipIgnored(text, cursor);
            while (text[cursor.Index] !== closing) {
                let key = String(index++);
                if (opening === '{') {
                    const keyStart = cursor.Index;
                    this.ReadString(text, cursor);
                    key = JSON.parse(text.slice(keyStart, cursor.Index));
                    this.SkipIgnored(text, cursor);
                    cursor.Index++; // The colon was checked by the validating parser.
                }
                children.set(key, this.Read(text, cursor));
                this.SkipIgnored(text, cursor);
                if (text[cursor.Index] === ',') {
                    cursor.Index++;
                    this.SkipIgnored(text, cursor);
                }
            }
            cursor.Index++;
        } else if (opening === '"') {
            this.ReadString(text, cursor);
        } else {
            while (cursor.Index < text.length && !/[\s,\]}\/]/.test(text[cursor.Index])) cursor.Index++;
        }
        return new JsonElementSource(text.slice(start, cursor.Index), children);
    }

    /** Skips a validated JSON string, treating escaped quotes as content. */
    private static ReadString(text: string, cursor: { Index: number }): void {
        cursor.Index++;
        while (cursor.Index < text.length) {
            const character = text[cursor.Index++];
            if (character === '\\') cursor.Index++;
            else if (character === '"') return;
        }
    }

    /** Skips whitespace and comments already admitted by the document options. */
    private static SkipIgnored(text: string, cursor: { Index: number }): void {
        while (cursor.Index < text.length) {
            if (/\s/.test(text[cursor.Index])) cursor.Index++;
            else if (text.startsWith('//', cursor.Index)) {
                cursor.Index += 2;
                while (cursor.Index < text.length && !/[\r\n]/.test(text[cursor.Index])) cursor.Index++;
            } else if (text.startsWith('/*', cursor.Index)) {
                cursor.Index = text.indexOf('*/', cursor.Index + 2) + 2;
            } else return;
        }
    }
}
