// @ts-nocheck
import { IDisposable } from "../../disposable.interface";
import { sha1 } from "@noble/hashes/sha1";
import { sha256 } from "@noble/hashes/sha256";
import { md5 } from "@noble/hashes/legacy";

type HashFunction = (data: Uint8Array) => Uint8Array;

export class HashAlgorithm implements IDisposable {
    private readonly algorithm: string;
    private readonly hashFunction: HashFunction;

    private constructor(algorithm: string, hashFunction: HashFunction) {
        this.algorithm = algorithm.toUpperCase();
        this.hashFunction = hashFunction;
    }

    dispose(): void {
    }

    public static create(algorithm: string): HashAlgorithm | null {
        const normalized = algorithm.toUpperCase();
        switch (normalized) {
            case "SHA-256":
                return new HashAlgorithm(normalized, sha256);
            case "SHA-1":
                return new HashAlgorithm(normalized, sha1);
            case "MD5":
                return new HashAlgorithm(normalized, md5);
            default:
                return null;
        }
    }

    public async computeHash(data: Uint8Array): Promise<Uint8Array> {
        return new Uint8Array(this.hashFunction(data));
    }
}
