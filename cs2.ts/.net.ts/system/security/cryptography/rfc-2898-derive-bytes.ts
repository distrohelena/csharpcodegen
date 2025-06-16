import { pbkdf2Sync, createHash } from 'crypto';

/**
 * Mimics the C# Rfc2898DeriveBytes class using Node.js crypto.
 */
export class Rfc2898DeriveBytes {
    private password: Buffer;
    private salt: Buffer;
    private iterations: number;
    private hashAlgorithm: string;

    constructor(password: string | Buffer, salt: Buffer, iterations: number, hashAlgorithm: string = 'sha256') {
        this.password = typeof password === 'string' ? Buffer.from(password, 'utf8') : password;
        this.salt = salt;
        this.iterations = iterations;
        this.hashAlgorithm = hashAlgorithm.toLowerCase();
    }

    /**
     * Derives a key of the specified number of bytes.
     * @param keySizeInBytes - Desired size of the key in bytes.
     * @returns A Buffer containing the derived key.
     */
    public getBytes(keySizeInBytes: number): Buffer {
        return pbkdf2Sync(this.password, this.salt, this.iterations, keySizeInBytes, this.hashAlgorithm);
    }
}
