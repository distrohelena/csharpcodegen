import { Stream } from "./stream";
import { SeekOrigin } from "./seek-origin";

export class MemoryStream extends Stream {
    get CanRead(): boolean {
        return true;
    }
    get CanWrite(): boolean {
        return true;
    }
    get CanSeek(): boolean {
        return true;
    }
    get Length(): number {
        return this.buffer.length;
    }

    get Position(): number {
        return this.position;
    }
    set Position(value: number) {
        this.position = value;
    }

    private buffer: Uint8Array;
    private position: number;

    constructor(initialSize: number = 256) {
        super();

        this.position = 0;
        this.buffer = new Uint8Array(initialSize);
    }

    override InternalReserve(count: number) {
        const requiredSize = this.position + count;
        if (requiredSize > this.buffer.length) {
            const newBuffer = new Uint8Array(Math.max(this.buffer.length * 2, requiredSize));
            newBuffer.set(this.buffer, 0);
            this.buffer = newBuffer;
        }
    }

    override InternalWriteByte(byte: number): void {
        this.buffer[this.position] = byte;
        this.position += 1;
    }

    override InternalReadByte(): number {
        let value = this.buffer[this.position];
        this.position++;
        return value;
    }

    override Read(buffer: Uint8Array, offset: number, count: number): number {
        const remaining = this.buffer.length - this.position;
        const toRead = Math.min(count, remaining);

        if (toRead <= 0) {
            return 0;
        }

        buffer.set(this.buffer.subarray(this.position, this.position + toRead), offset);
        this.position += toRead;
        return toRead;
    }

    override Write(buffer: Uint8Array, offset: number, count: number): void {
        const requiredSize = this.position + count;
        if (requiredSize > this.buffer.length) {
            const newBuffer = new Uint8Array(Math.max(this.buffer.length * 2, requiredSize));
            newBuffer.set(this.buffer, 0);
            this.buffer = newBuffer;
        }

        this.buffer.set(buffer.subarray(offset, offset + count), this.position);
        this.position += count;
    }

    override Seek(offset: number, origin: SeekOrigin): number {
        let newPosition: number;

        switch (origin) {
            case SeekOrigin.Begin:
                newPosition = offset;
                break;
            case SeekOrigin.Current:
                newPosition = this.position + offset;
                break;
            case SeekOrigin.End:
                newPosition = this.buffer.length + offset;
                break;
            default:
                throw new Error("Invalid SeekOrigin.");
        }

        if (newPosition < 0 || newPosition > this.buffer.length) {
            throw new RangeError("Seek position out of bounds.");
        }

        this.position = newPosition;
        return this.position;
    }

    SetLength(length: number): void {
        if (length < this.buffer.length) {
            this.buffer = this.buffer.subarray(0, length);
        } else {
            const newBuffer = new Uint8Array(length);
            newBuffer.set(this.buffer, 0);
            this.buffer = newBuffer;
        }
    }

    GetLength(): number {
        return this.buffer.length;
    }

    Close(): void {
        this.buffer = new Uint8Array(0); // Clear buffer
        this.position = 0;
    }

    ToArray(): Uint8Array {
        return this.buffer.subarray(0, this.position);
    }
}
