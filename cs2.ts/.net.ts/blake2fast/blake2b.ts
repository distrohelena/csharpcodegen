var blake2b = require('blake2b');

export class Blake2b {
    static computeHash(hashSize: number, buffer: Buffer): Uint8Array {
        var output = new Uint8Array(64);

        blake2b(output.length).update(buffer).digest('hex');

        return output;
    }
}