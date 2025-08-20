export class NativeArrayUtil {
    static copy(src: Uint8Array, srcOffset: number, dest: Uint8Array, destOffset: number, length: number): void {
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