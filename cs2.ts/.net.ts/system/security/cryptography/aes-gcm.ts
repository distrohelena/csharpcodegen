import { createCipheriv, randomBytes } from 'crypto';

/**
 * A class to perform AES-GCM encryption.
 */
class AesGcm {
    private key: Buffer;

    constructor(key: Buffer) {
        if (![16, 24, 32].includes(key.length)) {
            throw new Error("Key must be 128, 192, or 256 bits long.");
        }
        this.key = key;
    }

    /**
     * Encrypts data using AES-GCM.
     * @param iv - Initialization vector (Buffer, usually 12 bytes).
     * @param plaintext - Data to encrypt.
     * @returns An object containing ciphertext and tag.
     */
    public encrypt(iv: Buffer, plaintext: Buffer): { ciphertext: Buffer; tag: Buffer } {
        const cipher = createCipheriv('aes-' + (this.key.length * 8) + '-gcm', this.key, iv);
        const ciphertext = Buffer.concat([cipher.update(plaintext), cipher.final()]);
        const tag = cipher.getAuthTag();
        return { ciphertext, tag };
    }
}
