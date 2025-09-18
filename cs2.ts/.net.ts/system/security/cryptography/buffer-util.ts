// @ts-nocheck
﻿export function cloneToArrayBuffer(view: Uint8Array): ArrayBuffer {
    const copy = Uint8Array.from(view);
    return copy.buffer;
}

export function concatUint8Arrays(...arrays: Uint8Array[]): Uint8Array {
    const total = arrays.reduce((sum, arr) => sum + arr.length, 0);
    const result = new Uint8Array(total);
    let offset = 0;
    for (const arr of arrays) {
        result.set(arr, offset);
        offset += arr.length;
    }
    return result;
}
