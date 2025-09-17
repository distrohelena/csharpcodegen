import { createHash } from "crypto";

export class Blake2b {
    /**
     * Computes a BLAKE2b hash.
     * @param hashSize Output hash size in bytes (up to 64)
     * @param buffer Input data
     * @returns Hash as Uint8Array
     */
    static computeHash(hashSize: number, buffer: Uint8Array): Uint8Array {
        if (hashSize <= 0 || hashSize > 64) {
            throw new RangeError("hashSize must be between 1 and 64 bytes.");
        }

        const digest = createHash("blake2b512").update(Buffer.from(buffer)).digest();
        return new Uint8Array(digest).subarray(0, hashSize);
    }
}
