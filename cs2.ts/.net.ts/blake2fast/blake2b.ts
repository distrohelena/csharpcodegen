// @ts-nocheck
﻿import { blake2b } from "@noble/hashes/blake2b";

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

        const digest = blake2b(buffer, { dkLen: hashSize });
        return new Uint8Array(digest);
    }
}
