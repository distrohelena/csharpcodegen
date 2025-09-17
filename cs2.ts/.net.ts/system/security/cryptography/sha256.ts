import { createHash } from "crypto";

export class SHA256 {
    static async hashData(data: Uint8Array): Promise<Uint8Array> {
        const hash = createHash("sha256").update(Buffer.from(data)).digest();
        return new Uint8Array(hash);
    }
}
