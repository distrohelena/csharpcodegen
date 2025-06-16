import { createHmac } from 'crypto';

class HMACSHA256 {
    private key: Buffer;

    constructor(key: Buffer) {
        this.key = key;
    }

    /**
     * Computes the HMAC-SHA256 hash of the input data.
     * @param data - The data to hash (Buffer).
     * @returns A Buffer with the HMAC digest.
     */
    public computeHash(data: Buffer): Buffer {
        return createHmac('sha256', this.key).update(data).digest();
    }
}
