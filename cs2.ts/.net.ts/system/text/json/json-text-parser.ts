/** Normalizes only explicitly permitted JSON syntax, leaving string contents and invalid tokens intact. */
export class JsonTextParser {
    /** Removes comments outside strings and then optional trailing commas before strict JSON parsing. */
    public static Parse(text: string, skipComments: boolean, allowTrailingCommas: boolean): unknown {
        const uncommented = this.RemoveComments(text, skipComments);
        return JSON.parse(allowTrailingCommas ? this.RemoveTrailingCommas(uncommented) : uncommented);
    }

    /** Replaces permitted comments with whitespace so adjacent tokens cannot accidentally join. */
    private static RemoveComments(text: string, skipComments: boolean): string {
        const output: string[] = [];
        let inString = false;
        for (let index = 0; index < text.length; index++) {
            const char = text[index];
            if (inString) {
                output.push(char);
                if (char === '\\') { output.push(text[++index] || ''); }
                else if (char === '"') { inString = false; }
            } else if (char === '"') {
                inString = true;
                output.push(char);
            } else if (char === '/' && (text[index + 1] === '/' || text[index + 1] === '*')) {
                if (!skipComments) { throw new SyntaxError('JSON comments are not permitted.'); }
                const lineComment = text[index + 1] === '/';
                output.push('  ');
                index += 2;
                if (lineComment) {
                    while (index < text.length && text[index] !== '\r' && text[index] !== '\n') {
                        output.push(' ');
                        index++;
                    }
                    index--;
                } else {
                    while (index < text.length && !(text[index] === '*' && text[index + 1] === '/')) {
                        output.push(text[index] === '\r' || text[index] === '\n' ? text[index] : ' ');
                        index++;
                    }
                    if (index >= text.length) { throw new SyntaxError('JSON block comment is not terminated.'); }
                    output.push('  ');
                    index++;
                }
            } else {
                output.push(char);
            }
        }
        return output.join('');
    }

    /** Removes final commas only outside strings, preserving doubled commas and empty-element errors. */
    private static RemoveTrailingCommas(text: string): string {
        const output: string[] = [];
        let inString = false;
        let previous = '';
        for (let index = 0; index < text.length; index++) {
            const char = text[index];
            if (inString) {
                output.push(char);
                if (char === '\\') { output.push(text[++index] || ''); }
                else if (char === '"') { inString = false; previous = '"'; }
                continue;
            }
            if (char === '"') { inString = true; }
            if (char === ',' && previous !== '' && !'[{,:'.includes(previous)) {
                let next = index + 1;
                while (next < text.length && /[\t\n\r ]/.test(text[next])) { next++; }
                if (text[next] === ']' || text[next] === '}') {
                    output.push(' ');
                    previous = ',';
                    continue;
                }
            }
            output.push(char);
            if (!/[\t\n\r ]/.test(char)) { previous = char; }
        }
        return output.join('');
    }
}
