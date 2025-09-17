export class NativeArrayUtil {
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
\n\ndeclare global {\n    interface Uint8Array {\n        AsSpan(start?: number, length?: number): Uint8Array;\n    }\n}\n\nUint8Array.prototype.AsSpan = function(start: number = 0, length?: number): Uint8Array {\n    const end = length === undefined ? undefined : start + length;\n    return this.subarray(start, end);\n};\n
