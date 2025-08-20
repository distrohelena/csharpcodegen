import { Stream } from "./stream";
import { SeekOrigin } from "./seek-origin";

export class MemoryStream extends Stream {
    get canRead(): boolean {
        return true;
    }
    get canWrite(): boolean {
        return true;
    }
    get canSeek(): boolean {
        return true;
    }
    get length(): number {
        return this.buffer.length;
    }

    get position(): number {
        return this._position;
    }
    set position(value: number) {
        this._position = value;
    }

    private buffer: Uint8Array;
    private _position: number;

    constructor(buffer?: Uint8Array);
    constructor(buffer: Uint8Array, writable: boolean);
    constructor(buffer: Uint8Array, index: number, count: number);
    constructor(buffer: Uint8Array, index: number, count: number, writable: boolean);
    constructor(buffer: Uint8Array, index: number, count: number, writable: boolean, publiclyVisible: boolean);
    constructor(initialSizeOrBuffer?: number | Uint8Array, index?: number | boolean, count?: number, writable: boolean = true, publiclyVisible: boolean = false) {
        super();

        this._position = 0;

        if (initialSizeOrBuffer === undefined) {
            this.buffer = new Uint8Array(0); // or an initial default size like new Uint8Array(256)
            return;
        }

        if (typeof initialSizeOrBuffer === "number") {
            // constructor(initialSize: number)
            this.buffer = new Uint8Array(initialSizeOrBuffer);
        } else {
            const buffer = initialSizeOrBuffer;

            if (typeof index === "boolean") {
                // constructor(buffer: Uint8Array, writable: boolean)
                this.buffer = buffer;
                this._position = 0;
            } else if (typeof index === "number" && typeof count === "number") {
                // constructor(buffer: Uint8Array, index: number, count: number [, writable] [, publiclyVisible])
                if (index < 0 || count < 0 || index + count > buffer.length) {
                    throw new RangeError("Index and count must be within the bounds of the buffer.");
                }

                this.buffer = buffer.subarray(index, index + count);
            } else {
                // constructor(buffer: Uint8Array)
                this.buffer = buffer;
            }
        }
    }

    override internalReserve(count: number) {
        const requiredSize = this.position + count;
        if (requiredSize > this.buffer.length) {
            const newBuffer = new Uint8Array(Math.max(this.buffer.length * 2, requiredSize));
            newBuffer.set(this.buffer, 0);
            this.buffer = newBuffer;
        }
    }

    override internalWriteByte(byte: number): void {
        this.buffer[this.position] = byte;
        this.position += 1;
    }

    override internalReadByte(): number {
        let value = this.buffer[this.position];
        this.position++;
        return value;
    }

    override read(buffer: Uint8Array, offset: number, count: number): number {
        const remaining = this.buffer.length - this.position;
        const toRead = Math.min(count, remaining);

        if (toRead <= 0) {
            return 0;
        }

        buffer.set(this.buffer.subarray(this.position, this.position + toRead), offset);
        this.position += toRead;
        return toRead;
    }

    override write(buffer: Uint8Array, offset: number, count: number): void {
        const requiredSize = this.position + count;
        if (requiredSize > this.buffer.length) {
            const newBuffer = new Uint8Array(Math.max(this.buffer.length * 2, requiredSize));
            newBuffer.set(this.buffer, 0);
            this.buffer = newBuffer;
        }

        this.buffer.set(buffer.subarray(offset, offset + count), this.position);
        this.position += count;
    }

    override seek(offset: number, origin: SeekOrigin): number {
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

    setLength(length: number): void {
        if (length < this.buffer.length) {
            this.buffer = this.buffer.subarray(0, length);
        } else {
            const newBuffer = new Uint8Array(length);
            newBuffer.set(this.buffer, 0);
            this.buffer = newBuffer;
        }
    }

    getLength(): number {
        return this.buffer.length;
    }

    close(): void {
        this.buffer = new Uint8Array(0); // Clear buffer
        this.position = 0;
    }

    toArray(): Uint8Array {
        return this.buffer.subarray(0, this.position);
    }
}
