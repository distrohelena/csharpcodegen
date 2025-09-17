import { IDisposable } from "../../disposable.interface";
import { createHash } from "crypto";

/**
 * A utility class to compute cryptographic hashes using Node.js crypto.
 */
export class HashAlgorithm implements IDisposable {
    private algorithm: string;

    private constructor(algorithm: string) {
        this.algorithm = algorithm.toUpperCase();
    }

    dispose(): void {
    }

    public static create(algorithm: string): HashAlgorithm | null {
        const normalized = algorithm.toUpperCase();
        const supported = new Set(["SHA-256", "SHA-1", "MD5"]);
        if (supported.has(normalized)) {
            return new HashAlgorithm(normalized);
        }
        return null;
    }

    public async computeHash(data: Uint8Array): Promise<Uint8Array> {
        const nodeAlgorithm = this.algorithm.replace(/-/g, "").toLowerCase();
        const hash = createHash(nodeAlgorithm).update(Buffer.from(data)).digest();
        return new Uint8Array(hash);
    }
}
