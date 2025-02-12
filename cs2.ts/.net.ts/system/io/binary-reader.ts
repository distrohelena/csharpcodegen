import { Stream } from "./stream";

export class BinaryReader {
    private stream: Stream;

    constructor(stream: Stream) {
        this.stream = stream;
    }

    readByte(): number {
        return this.stream.InternalReadByte();
    }

    readInt8(): number {
        const value = this.stream.InternalReadByte();
        return value < 0x80 ? value : value - 0x100;
    }

    readUInt16(): number {
        const low = this.stream.InternalReadByte();
        const high = this.stream.InternalReadByte();
        return (high << 8) | low;
    }

    readInt16(): number {
        const value = this.readUInt16();
        return value < 0x8000 ? value : value - 0x10000;
    }

    readUInt32(): number {
        const b1 = this.stream.InternalReadByte();
        const b2 = this.stream.InternalReadByte();
        const b3 = this.stream.InternalReadByte();
        const b4 = this.stream.InternalReadByte();
        return (b4 << 24) | (b3 << 16) | (b2 << 8) | b1;
    }

    readInt32(): number {
        const value = this.readUInt32();
        return value < 0x80000000 ? value : value - 0x100000000;
    }

    readUInt64(): bigint {
        const low = BigInt(this.readUInt32());
        const high = BigInt(this.readUInt32());
        return (high << 32n) | low;
    }

    readInt64(): bigint {
        const value = this.readUInt64();
        return value < 0x8000000000000000n ? value : value - 0x10000000000000000n;
    }

    readSingle(): number {
        const buffer = new Uint8Array(4);
        buffer[0] = this.stream.InternalReadByte();
        buffer[1] = this.stream.InternalReadByte();
        buffer[2] = this.stream.InternalReadByte();
        buffer[3] = this.stream.InternalReadByte();
        return new DataView(buffer.buffer).getFloat32(0, true); // Little-endian
    }

    readDouble(): number {
        const buffer = new Uint8Array(8);
        for (let i = 0; i < 8; i++) {
            buffer[i] = this.stream.InternalReadByte();
        }
        return new DataView(buffer.buffer).getFloat64(0, true); // Little-endian
    }

    readBytes(length: number): Uint8Array {
        const buffer = new Uint8Array(length);
        for (let i = 0; i < length; i++) {
            buffer[i] = this.stream.InternalReadByte();
        }
        return buffer;
    }

    readString(): string {
        const length = this.readByte();
        const buffer = this.readBytes(length);
        return new TextDecoder("utf-8").decode(buffer);
    }

    readStringZeroUtf8(): string {
        const bytes = [];
        while (true) {
            const byte = this.stream.InternalReadByte();
            if (byte === 0) break;
            bytes.push(byte);
        }
        return new TextDecoder("utf-8").decode(new Uint8Array(bytes));
    }

    readArray(): Uint8Array {
        const count = this.readUInt32();
        return count === 0 ? new Uint8Array(0) : this.readBytes(count);
    }

    readBoolean(): boolean {
        return this.readByte() !== 0;
    }
}
