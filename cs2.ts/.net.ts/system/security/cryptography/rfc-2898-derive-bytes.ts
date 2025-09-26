// @ts-nocheck
﻿import { pbkdf2 } from "@noble/hashes/pbkdf2";
import { sha1 } from "@noble/hashes/sha1";
import { sha256 } from "@noble/hashes/sha256";
import { sha384, sha512 } from "@noble/hashes/sha512";
import { IDisposable } from "../../disposable.interface";
import { HashAlgorithmName } from "./hash-algorithm-name";

type HashFunction = (msg: Uint8Array) => Uint8Array;

function resolveHashFunction(hashAlgorithm: HashAlgorithmName): HashFunction {
    switch (hashAlgorithm) {
        case HashAlgorithmName.SHA1:
            return sha1;
        case HashAlgorithmName.SHA256:
            return sha256;
        case HashAlgorithmName.SHA384:
            return sha384;
        case HashAlgorithmName.SHA512:
            return sha512;
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
        const hashFn = resolveHashFunction(this.hashAlgorithm);
        const derived = pbkdf2(hashFn, this.password, this.salt, { c: this.iterations, dkLen: keySizeInBytes });
        return new Uint8Array(derived);
    }
}
