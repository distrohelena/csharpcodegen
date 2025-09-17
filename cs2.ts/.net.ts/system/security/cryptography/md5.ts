import { createHash } from "crypto";

export class MD5 {
    static Create(): MD5 {
        return new MD5();
    }

    static async hashData(data: Uint8Array): Promise<Uint8Array> {
        const hash = createHash("md5").update(Buffer.from(data)).digest();
        return new Uint8Array(hash);
    }

    async computeHash(data: Uint8Array): Promise<Uint8Array> {
        return MD5.hashData(data);
    }
}
