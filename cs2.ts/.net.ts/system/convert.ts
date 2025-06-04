export class Convert {

    static fromBase64String(base64: string): Uint8Array {
        if (typeof atob === "function") {
            // Browser
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
            }
            return bytes;
        } else {
            // Node.js
            return Uint8Array.from(Buffer.from(base64, "base64"));
        }
    }
}