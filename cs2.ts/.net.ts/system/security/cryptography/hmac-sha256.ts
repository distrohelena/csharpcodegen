// @ts-nocheck
﻿import { hmac } from "@noble/hashes/hmac";
import { sha256 } from "@noble/hashes/sha256";
import { IDisposable } from "../../disposable.interface";

export class HMACSHA256 implements IDisposable {
    private readonly key: Uint8Array;

    constructor(key: Uint8Array) {
        this.key = key;
    }

    dispose(): void {
    }

    async computeHash(data: Uint8Array): Promise<Uint8Array> {
        return new Uint8Array(hmac(sha256, this.key, data));
    }
}
