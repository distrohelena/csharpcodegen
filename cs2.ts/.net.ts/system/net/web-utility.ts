/** HTML text encoding compatible with System.Net.WebUtility. */
export class WebUtility {
    /** Escapes markup and Latin-1 entities, preserving .NET UTF-16 surrogate handling. */
    static HtmlEncode(value: string): string {
        if (value == null || value.length === 0) return value;
        let encoded = '';
        for (let index = 0; index < value.length; index += 1) {
            const code = value.charCodeAt(index);
            if (code === 34) encoded += '&quot;';
            else if (code === 38) encoded += '&amp;';
            else if (code === 39) encoded += '&#39;';
            else if (code === 60) encoded += '&lt;';
            else if (code === 62) encoded += '&gt;';
            else if (code >= 160 && code <= 255) encoded += `&#${code};`;
            else if (code >= 0xd800 && code <= 0xdbff) {
                const low = value.charCodeAt(index + 1);
                if (low >= 0xdc00 && low <= 0xdfff) {
                    encoded += `&#${0x10000 + ((code - 0xd800) << 10) + low - 0xdc00};`;
                    index += 1;
                } else encoded += '\ufffd';
            } else if (code >= 0xdc00 && code <= 0xdfff) encoded += '\ufffd';
            else encoded += value[index];
        }
        return encoded;
    }
}