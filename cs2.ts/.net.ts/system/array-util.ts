export class ArrayUtil {
    static copy(src: Uint8Array, srcOffset: number, dest: Uint8Array, destOffset: number, length: number): void {
        dest.set(src.subarray(srcOffset, srcOffset + length), destOffset);
    }
}