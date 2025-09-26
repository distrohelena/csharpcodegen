// @ts-nocheck
﻿import { IDisposable } from "../../disposable.interface";
import { concatUint8Arrays } from "./buffer-util";
import { getSubtleCrypto, toArrayBuffer } from "./web-crypto";

/**
 * A class to perform AES-GCM encryption.
 */
export class AesGcm implements IDisposable {
    private key: Uint8Array;

    constructor(key: Uint8Array) {
        this.key = key;
    }

    dispose(): void {
    }

    /**
     * Encrypts data using AES-GCM.
     * @param iv - Initialization vector (Buffer, usually 12 bytes).
     * @param data - Data to encrypt.
     * @param cipherText - Buffer to receive ciphertext.
     * @param tag - Buffer to receive authentication tag (16 bytes).
     */
    public async encrypt(iv: Uint8Array, data: Uint8Array, cipherText: Uint8Array, tag: Uint8Array): Promise<void> {
        const subtle = getSubtleCrypto();
        const cryptoKey = await subtle.importKey(
            "raw",
            toArrayBuffer(this.key),
            { name: "AES-GCM" },
            false,
            ["encrypt"]
        );

        const encrypted = new Uint8Array(
            await subtle.encrypt(
                {
                    name: "AES-GCM",
                    iv: toArrayBuffer(iv),
                    tagLength: 128 // 16 bytes
                },
                cryptoKey,
                toArrayBuffer(data)
            )
        );

        const tagLength = 16; // bytes
        const ciphertextLength = encrypted.length - tagLength;

        cipherText.set(encrypted.subarray(0, ciphertextLength));
        tag.set(encrypted.subarray(ciphertextLength));
    }

    /**
     * Decrypts data using AES-GCM.
     * @param iv - Initialization vector (12 bytes).
     * @param ciphertext - Encrypted data (excluding the tag).
     * @param tag - Authentication tag (16 bytes).
     * @param output - Buffer to receive the decrypted plaintext.
     */
    public async decrypt(iv: Uint8Array, ciphertext: Uint8Array, tag: Uint8Array, output: Uint8Array): Promise<void> {
        const subtle = getSubtleCrypto();
        const cryptoKey = await subtle.importKey(
            "raw",
            toArrayBuffer(this.key),
            { name: "AES-GCM" },
            false,
            ["decrypt"]
        );

        const combined = concatUint8Arrays(ciphertext, tag);

        const decrypted = new Uint8Array(
            await subtle.decrypt(
                {
                    name: "AES-GCM",
                    iv: toArrayBuffer(iv),
                    tagLength: 128 // 16 bytes
                },
                cryptoKey,
                toArrayBuffer(combined)
            )
        );

        output.set(decrypted);
    }
}
