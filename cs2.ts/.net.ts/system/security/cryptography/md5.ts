// @ts-nocheck
﻿import { md5 } from "@noble/hashes/legacy";

export class MD5 {
    static Create(): MD5 {
        return new MD5();
    }

    static async hashData(data: Uint8Array): Promise<Uint8Array> {
        return new Uint8Array(md5(data));
    }

    async computeHash(data: Uint8Array): Promise<Uint8Array> {
        return MD5.hashData(data);
    }
}
