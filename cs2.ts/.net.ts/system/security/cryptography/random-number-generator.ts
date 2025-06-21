import { IDisposable } from "../../disposable.interface";

export class RandomNumberGenerator implements IDisposable {
    static create(): RandomNumberGenerator {
        return new RandomNumberGenerator();
    }

    dispose(): void {
    }

    getBytes(buffer: Uint8Array) {
        if (typeof window !== "undefined" && window.crypto?.getRandomValues) {
            // Browser
            window.crypto.getRandomValues(buffer);
        } else {
            // Node.js
            const nodeCrypto = require("crypto");
            const bytes = nodeCrypto.randomBytes(length);
            buffer.set(bytes);
        }
    }

    getInt(min: number, max: number): number {
        if (min >= max) throw new Error("min must be less than max");

        const range = max - min;
        const maxUint32 = 0xFFFFFFFF;
        const maxAcceptable = maxUint32 - (maxUint32 % range);

        let rand: number;
        do {
            const buf = this.getBytes(4);
            rand = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
            rand = rand >>> 0; // Force unsigned
        } while (rand > maxAcceptable);

        return min + (rand % range);
    }
}