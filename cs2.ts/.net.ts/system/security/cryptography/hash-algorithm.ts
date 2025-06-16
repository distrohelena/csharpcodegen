import { createHash } from 'crypto';

/**
 * A utility class to compute cryptographic hashes.
 */
export class HashAlgorithm {
    private algorithm: string;

    private constructor(algorithm: string) {
        this.algorithm = algorithm.toLowerCase();
    }

    /**
     * Factory method to create a HashAlgorithm instance.
     * @param algorithm - Name of the hash algorithm (e.g., "sha256")
     * @returns A HashAlgorithm instance or null if unsupported.
     */
    public static create(algorithm: string): HashAlgorithm | null {
        const supported = ['sha256', 'sha1', 'md5']; // add more as needed
        if (supported.includes(algorithm.toLowerCase())) {
            return new HashAlgorithm(algorithm);
        }
        return null;
    }

    /**
     * Computes the hash of the given input bytes.
     * @param data - The data to hash.
     * @returns A Buffer containing the hash.
     */
    public computeHash(data: Buffer): Buffer {
        return createHash(this.algorithm).update(data).digest();
    }
}
