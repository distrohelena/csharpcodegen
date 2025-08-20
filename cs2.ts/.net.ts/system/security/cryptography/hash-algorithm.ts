import { IDisposable } from '../../disposable.interface';

/**
 * A utility class to compute cryptographic hashes using Web Crypto API.
 */
export class HashAlgorithm implements IDisposable {
    private algorithm: string;

    private constructor(algorithm: string) {
        this.algorithm = algorithm.toUpperCase();
    }

    dispose(): void {
    }

    /**
     * Factory method to create a HashAlgorithm instance.
     * @param algorithm - Name of the hash algorithm (e.g., "SHA-256")
     * @returns A HashAlgorithm instance or null if unsupported.
     */
    public static create(algorithm: string): HashAlgorithm | null {
        const supported = ['SHA-256', 'SHA-1']; // Web Crypto does NOT support MD5
        if (supported.includes(algorithm.toUpperCase())) {
            return new HashAlgorithm(algorithm);
        }
        return null;
    }

    /**
     * Computes the hash of the given input bytes using Web Crypto.
     * @param data - The data to hash.
     * @returns A Promise resolving to a Uint8Array containing the hash.
     */
    public async computeHash(data: Uint8Array): Promise<Uint8Array> {
        const hashBuffer = await crypto.subtle.digest(this.algorithm, data);
        return new Uint8Array(hashBuffer);
    }
}
