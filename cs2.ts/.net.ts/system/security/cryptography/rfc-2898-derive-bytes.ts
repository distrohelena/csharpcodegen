import { pbkdf2Sync } from "crypto";
import { IDisposable } from "../../disposable.interface";
import { HashAlgorithmName } from "./hash-algorithm-name";

function toNodeAlgorithm(hashAlgorithm: HashAlgorithmName): string {
    switch (hashAlgorithm) {
        case HashAlgorithmName.SHA1:
            return "sha1";
        case HashAlgorithmName.SHA256:
            return "sha256";
        case HashAlgorithmName.SHA384:
            return "sha384";
        case HashAlgorithmName.SHA512:
            return "sha512";
        default:
            throw new Error(`Unsupported hash algorithm: ${hashAlgorithm}`);
    }
}

export class Rfc2898DeriveBytes implements IDisposable {
    constructor(
        private readonly password: Uint8Array,
        private readonly salt: Uint8Array,
        private readonly iterations: number,
        private readonly hashAlgorithm: HashAlgorithmName
    ) {}

    dispose(): void {}

    public async getBytes(keySizeInBytes: number): Promise<Uint8Array> {
        const algo = toNodeAlgorithm(this.hashAlgorithm);
        const derived = pbkdf2Sync(Buffer.from(this.password), Buffer.from(this.salt), this.iterations, keySizeInBytes, algo);
        return new Uint8Array(derived);
    }
}
