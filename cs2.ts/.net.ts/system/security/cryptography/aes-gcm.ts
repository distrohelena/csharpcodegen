// @ts-nocheck
﻿import { IDisposable } from "../../disposable.interface";
import { concatUint8Arrays } from "./buffer-util";
import { getSubtleCrypto, toArrayBuffer } from "./web-crypto";

/**
 * A class to perform AES-GCM encryption.
 */
export class AesGcm implements IDisposable {
    private key: Uint8Array;
    private readonly tagLengthBytes: number;

    constructor(key: Uint8Array, tagLengthBytes: number = 16) {
        if (![12, 13, 14, 15, 16].includes(tagLengthBytes)) {
            throw new RangeError("AES-GCM tag length must be between 12 and 16 bytes.");
        }
        this.key = key;
        this.tagLengthBytes = tagLengthBytes;
    }

    dispose(): void {
    }

    /**
     * Encrypts data using AES-GCM.
     * @param iv - Initialization vector (Buffer, usually 12 bytes).
     * @param data - Data to encrypt.
     * @param cipherText - Buffer to receive ciphertext.
     * @param tag - Buffer to receive authentication tag (16 bytes).
     * @param associatedData - Optional additional authenticated data.
     */
    public async encrypt(iv: Uint8Array, data: Uint8Array, cipherText: Uint8Array, tag: Uint8Array, associatedData?: Uint8Array): Promise<void> {
        const subtle = getSubtleCrypto();
        const cryptoKey = await subtle.importKey(
            "raw",
            toArrayBuffer(this.key),
            { name: "AES-GCM" },
            false,
            ["encrypt"]
        );

        const params: AesGcmParams = {
            name: "AES-GCM",
            iv: toArrayBuffer(iv),
            tagLength: this.tagLengthBytes * 8
        };
        if (associatedData && associatedData.length > 0) {
            params.additionalData = toArrayBuffer(associatedData);
        }

        const encrypted = new Uint8Array(
            await subtle.encrypt(
                params,
                cryptoKey,
                toArrayBuffer(data)
            )
        );

        const ciphertextLength = encrypted.length - this.tagLengthBytes;

        cipherText.set(encrypted.subarray(0, ciphertextLength));
        tag.set(encrypted.subarray(ciphertextLength));
    }

    /**
     * Decrypts data using AES-GCM.
     * @param iv - Initialization vector (12 bytes).
     * @param ciphertext - Encrypted data (excluding the tag).
     * @param tag - Authentication tag (16 bytes).
     * @param output - Buffer to receive the decrypted plaintext.
     * @param associatedData - Optional additional authenticated data.
     */
    public async decrypt(iv: Uint8Array, ciphertext: Uint8Array, tag: Uint8Array, output: Uint8Array, associatedData?: Uint8Array): Promise<void> {
        const subtle = getSubtleCrypto();
        const cryptoKey = await subtle.importKey(
            "raw",
            toArrayBuffer(this.key),
            { name: "AES-GCM" },
            false,
            ["decrypt"]
        );

        const combined = concatUint8Arrays(ciphertext, tag);

        const params: AesGcmParams = {
            name: "AES-GCM",
            iv: toArrayBuffer(iv),
            tagLength: this.tagLengthBytes * 8
        };
        if (associatedData && associatedData.length > 0) {
            params.additionalData = toArrayBuffer(associatedData);
        }

        const decrypted = new Uint8Array(
            await subtle.decrypt(
                params,
                cryptoKey,
                toArrayBuffer(combined)
            )
        );

        output.set(decrypted);
    }
}
