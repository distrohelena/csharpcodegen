import { BinaryLike } from 'crypto';
import { IDisposable } from '../../disposable.interface';

export class HMACSHA256 implements IDisposable {
    private key: Uint8Array;

    constructor(key: Uint8Array) {
        this.key = key;
    }

    dispose(): void {
    }

    async computeHash(data: Uint8Array): Promise<Uint8Array> {
        // Import the key
        const cryptoKey = await crypto.subtle.importKey(
            'raw',
            this.key,
            { name: 'HMAC', hash: 'SHA-256' },
            false,
            ['sign']
        );

        // Sign the data (this computes HMAC)
        const signature = await crypto.subtle.sign(
            'HMAC',
            cryptoKey,
            data
        );

        return new Uint8Array(signature);
    }

}
