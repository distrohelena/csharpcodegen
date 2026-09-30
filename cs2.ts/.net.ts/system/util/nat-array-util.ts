// @ts-nocheck
﻿export class NativeArrayUtil {
    /** Copies byte ranges, including overlapping views, without relying on a Node Buffer global. */
    static blockCopy(source: ArrayBuffer | ArrayBufferView, sourceOffset: number,
        destination: ArrayBuffer | ArrayBufferView, destinationOffset: number, count: number): void {
        const sourceBytes = NativeArrayUtil.byteView(source);
        const destinationBytes = NativeArrayUtil.byteView(destination);
        if (!Number.isSafeInteger(sourceOffset) || !Number.isSafeInteger(destinationOffset) || !Number.isSafeInteger(count)
            || sourceOffset < 0 || destinationOffset < 0 || count < 0) {
            throw new RangeError("BlockCopy requires non-negative integer byte offsets and count.");
        }
        if (sourceOffset > sourceBytes.length - count || destinationOffset > destinationBytes.length - count) {
            throw new RangeError("BlockCopy byte range is outside the supplied arrays.");
        }
        destinationBytes.set(sourceBytes.subarray(sourceOffset, sourceOffset + count), destinationOffset);
    }

    /** Exposes the exact bytes of a primitive array view, preserving its offset and byte length. */
    private static byteView(value: ArrayBuffer | ArrayBufferView): Uint8Array {
        if (value instanceof ArrayBuffer) {
            return new Uint8Array(value);
        } else if (ArrayBuffer.isView(value)) {
            return new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
        }
        throw new TypeError("BlockCopy requires primitive array buffers or views.");
    }

    static copy(src: Uint8Array, dest: Uint8Array, length: number): void;
    static copy(src: Uint8Array, srcOffset: number, dest: Uint8Array, destOffset: number, length: number): void;
    static copy(src: Uint8Array, arg1: number | Uint8Array, arg2: Uint8Array | number, arg3?: number, arg4?: number): void {
        let srcOffset: number;
        let dest: Uint8Array;
        let destOffset: number;
        let length: number;

        if (arg1 instanceof Uint8Array) {
            srcOffset = 0;
            dest = arg1;
            destOffset = 0;
            length = typeof arg2 === "number" ? arg2 : dest.length;
        } else {
            srcOffset = (arg1 as number) ?? 0;
            if (!(arg2 instanceof Uint8Array)) {
                throw new TypeError("Destination array is required.");
            }
            dest = arg2;
            destOffset = arg3 ?? 0;
            length = arg4 ?? dest.length;
        }

        dest.set(src.subarray(srcOffset, srcOffset + length), destOffset);
    }

    /**
     * Constant-time comparison of two Uint8Arrays.
     * Returns true if they are equal in length and content.
     */
    static constantTimeSequenceEqual(a: Uint8Array, b: Uint8Array): boolean {
        if (a.length !== b.length) return false;

        let result = 0;
        for (let i = 0; i < a.length; i++) {
            result |= a[i] ^ b[i];
        }

        return result === 0;
    }
}


declare global {
    interface Uint8Array {
        AsSpan(start?: number, length?: number): Uint8Array;
        Clone(): Uint8Array;
    }
}

Uint8Array.prototype.AsSpan = function(start: number = 0, length?: number): Uint8Array {
    const end = length === undefined ? undefined : start + length;
    return this.subarray(start, end);
};

Uint8Array.prototype.Clone = function(): Uint8Array {
    return new Uint8Array(this);
};

