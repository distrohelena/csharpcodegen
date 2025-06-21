import { IDisposable } from '../../disposable.interface';

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
        const cryptoKey = await crypto.subtle.importKey(
            "raw",
            this.key,
            { name: "AES-GCM" },
            false,
            ["encrypt"]
        );

        const encrypted = new Uint8Array(
            await crypto.subtle.encrypt(
                {
                    name: "AES-GCM",
                    iv: iv,
                    tagLength: 128 // 16 bytes
                },
                cryptoKey,
                data
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
        const cryptoKey = await crypto.subtle.importKey(
            "raw",
            this.key,
            { name: "AES-GCM" },
            false,
            ["decrypt"]
        );

        // Combine ciphertext || tag
        const combined = new Uint8Array(ciphertext.length + tag.length);
        combined.set(ciphertext, 0);
        combined.set(tag, ciphertext.length);

        // Decrypt
        const decrypted = new Uint8Array(
            await crypto.subtle.decrypt(
                {
                    name: "AES-GCM",
                    iv: iv,
                    tagLength: 128 // 16 bytes
                },
                cryptoKey,
                combined
            )
        );

        output.set(decrypted);
    }
}
