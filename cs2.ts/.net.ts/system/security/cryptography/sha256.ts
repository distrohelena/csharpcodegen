// @ts-nocheck
﻿import { sha256 } from "@noble/hashes/sha256";

export class SHA256 {
    static async hashData(data: Uint8Array): Promise<Uint8Array> {
        return new Uint8Array(sha256(data));
    }
}
