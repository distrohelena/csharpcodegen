// @ts-nocheck
import { ArgumentNullException } from "../argument-null.exception";
export class Encoding {
    /** Counts the bytes emitted by this encoding, rejecting a null string like the CLR API. */
    GetByteCount(text: string): number {
        if (text == null) throw new ArgumentNullException("s");
        return this.getBytes(text).length;
    }

    /** Supports camel-cased calls emitted by encoding method translations. */
    getByteCount(text: string): number { return this.GetByteCount(text); }
    static get UTF8(): Encoding {
        return new Utf8Encoding();
    }

    static get ASCII(): Encoding {
        return new AsciiEncoding();
    }

    getBytes(_text: string): Uint8Array {
        throw new Error("Must be implemented by subclass");
    }

    getString(_bytes: Uint8Array): string {
        throw new Error("Must be implemented by subclass");
    }
}

class Utf8Encoding extends Encoding {
    private encoder = new TextEncoder();
    private decoder = new TextDecoder("utf-8");

    getBytes(text: string): Uint8Array {
        return this.encoder.encode(text);
    }

    getString(bytes: Uint8Array): string {
        return this.decoder.decode(bytes);
    }
}

class AsciiEncoding extends Encoding {
    getBytes(text: string): Uint8Array {
        const bytes = new Uint8Array(text.length);
        for (let i = 0; i < text.length; i++) {
            const charCode = text.charCodeAt(i);
            bytes[i] = charCode < 0x80 ? charCode : 0x3F; // 0x3F = '?'
        }
        return bytes;
    }

    getString(bytes: Uint8Array): string {
        return String.fromCharCode(...bytes);
    }
}
