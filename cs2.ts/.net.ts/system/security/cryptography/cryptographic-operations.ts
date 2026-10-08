/** Byte-span operations needed by portable identity/payment validation. */
export class CryptographicOperations {
    /** Compares equal-length spans without content-dependent early exit; JavaScript JIT timing is not guaranteed. */
    static FixedTimeEquals(left: Uint8Array, right: Uint8Array): boolean {
        if (left.length !== right.length) return false;
        let difference = 0;
        for (let index = 0; index < left.length; index++) difference |= left[index] ^ right[index];
        return difference === 0;
    }

    /** Clears the supplied view in place, including sliced views without touching adjacent bytes. */
    static ZeroMemory(buffer: Uint8Array): void {
        buffer.fill(0);
    }
}
