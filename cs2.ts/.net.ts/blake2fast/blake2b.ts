import { blake2b } from 'blakejs';

export class Blake2b {
    /**
     * Computes a BLAKE2b hash.
     * @param hashSize Output hash size in bytes (up to 64)
     * @param buffer Input data
     * @returns Hash as Uint8Array
     */
    static computeHash(hashSize: number, buffer: Uint8Array): Uint8Array {
        return blake2b(buffer, null, hashSize);
    }
}
