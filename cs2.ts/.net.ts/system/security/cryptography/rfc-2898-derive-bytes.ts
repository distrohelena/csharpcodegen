import { IDisposable } from '../../disposable.interface';
import { HashAlgorithmName } from './hash-algorithm-name';

// Helper to convert HashAlgorithmName to Web Crypto API format
function toWebCryptoHashAlgorithm(hashAlgorithm: HashAlgorithmName): string {
    switch (hashAlgorithm) {
        case HashAlgorithmName.SHA1:
            return 'SHA-1';
        case HashAlgorithmName.SHA256:
            return 'SHA-256';
        case HashAlgorithmName.SHA384:
            return 'SHA-384';
        case HashAlgorithmName.SHA512:
            return 'SHA-512';
        default:
            throw new Error(`Unsupported hash algorithm: ${hashAlgorithm}`);
    }
}

/**
 * Browser-safe PBKDF2 implementation using Web Crypto API.
 */
export class Rfc2898DeriveBytes implements IDisposable {
    private password: Uint8Array;
    private salt: Uint8Array;
    private iterations: number;
    private hashAlgorithm: string;

    constructor(password: Uint8Array, salt: Uint8Array, iterations: number, hashAlgorithm: HashAlgorithmName) {
        this.password = password;
        this.salt = salt;
        this.iterations = iterations;
        this.hashAlgorithm = toWebCryptoHashAlgorithm(hashAlgorithm);
    }

    dispose(): void {
    }

    /**
     * Derives a key of the specified number of bytes.
     * @param keySizeInBytes - Desired key size in bytes.
     * @returns A Promise resolving to the derived key as Uint8Array.
     */
    public async getBytes(keySizeInBytes: number): Promise<Uint8Array> {
        const keyMaterial = await crypto.subtle.importKey(
            'raw',
            this.password,
            { name: 'PBKDF2' },
            false,
            ['deriveBits']
        );

        const derivedBits = await crypto.subtle.deriveBits(
            {
                name: 'PBKDF2',
                salt: this.salt,
                iterations: this.iterations,
                hash: this.hashAlgorithm,
            },
            keyMaterial,
            keySizeInBytes * 8 // bits
        );

        return new Uint8Array(derivedBits);
    }
}
