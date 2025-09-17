import { createHmac } from "crypto";
import { IDisposable } from "../../disposable.interface";

export class HMACSHA256 implements IDisposable {
    private readonly key: Uint8Array;

    constructor(key: Uint8Array) {
        this.key = key;
    }

    dispose(): void {
    }

    async computeHash(data: Uint8Array): Promise<Uint8Array> {
        const mac = createHmac("sha256", Buffer.from(this.key))
            .update(Buffer.from(data))
            .digest();
        return new Uint8Array(mac);
    }
}
