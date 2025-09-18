// @ts-nocheck
﻿import { randomFillSync } from "crypto";
import { IDisposable } from "../../disposable.interface";

export class RandomNumberGenerator implements IDisposable {
    static create(): RandomNumberGenerator {
        return new RandomNumberGenerator();
    }

    dispose(): void {
    }

    getBytes(buffer: Uint8Array): void {
        randomFillSync(buffer);
    }

    getInt(min: number, max: number): number {
        if (min >= max) throw new Error("min must be less than max");

        const range = max - min;
        const maxUint32 = 0xFFFFFFFF;
        const maxAcceptable = maxUint32 - (maxUint32 % range);
        let rand: number;
        const buf = new Uint8Array(4);
        do {
            this.getBytes(buf);
            rand = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
            rand = rand >>> 0;
        } while (rand > maxAcceptable);

        return min + (rand % range);
    }
}
