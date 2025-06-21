import { BinaryLike, pbkdf2Sync } from 'crypto';
import { HashAlgorithmName } from './hash-algorithm-name';
import { IDisposable } from '../../disposable.interface';

/**
 * Mimics the C# Rfc2898DeriveBytes class using Node.js crypto.
 */
export class Rfc2898DeriveBytes implements IDisposable {
    private password: BinaryLike;
    private salt: BinaryLike;
    private iterations: number;
    private hashAlgorithm: string;

    constructor(password: BinaryLike, salt: BinaryLike, iterations: number, hashAlgorithm: HashAlgorithmName) {
        this.password = password;
        this.salt = salt;
        this.iterations = iterations;
        this.hashAlgorithm = hashAlgorithm.toLowerCase();
    }

    dispose(): void {
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
