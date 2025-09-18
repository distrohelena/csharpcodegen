// @ts-nocheck
﻿export class Convert {

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

    static toBase64String(bytes: Uint8Array): string {
        if (typeof btoa === "function") {
            // Browser
            let binary = "";
            for (let i = 0; i < bytes.length; i++) {
                binary += String.fromCharCode(bytes[i]);
            }
            return btoa(binary);
        } else {
            // Node.js
            return Buffer.from(bytes).toString("base64");
        }
    }
}
