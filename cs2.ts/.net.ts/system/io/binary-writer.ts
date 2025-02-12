import { Stream } from "./stream";

export class BinaryWriter {
    private stream: Stream;

    constructor(stream: Stream) {
        this.stream = stream;
    }

    writeByte(value: number): void {
        this.checkAlloc(1);
        this.stream.InternalWriteByte(value);
    }

    writeInt8(value: number): void {
        this.checkAlloc(1);
        this.stream.InternalWriteByte(value);
    }

    writeUInt16(value: number): void {
        this.checkAlloc(2);
        this.stream.InternalWriteByte(value);
        this.stream.InternalWriteByte(value >> 8);
    }

    writeInt16(value: number): void {
        this.checkAlloc(2);
        this.stream.InternalWriteByte(value);
        this.stream.InternalWriteByte(value >> 8);
    }

    writeUInt32(value: number): void {
        this.checkAlloc(4);
        this.stream.InternalWriteByte(value);
        this.stream.InternalWriteByte(value >> 8);
        this.stream.InternalWriteByte(value >> 16);
        this.stream.InternalWriteByte(value >> 24);
    }

    writeInt32(value: number): void {
        this.checkAlloc(4);
        this.stream.InternalWriteByte(value);
        this.stream.InternalWriteByte(value >> 8);
        this.stream.InternalWriteByte(value >> 16);
        this.stream.InternalWriteByte(value >> 24);
    }

    writeUInt64(value: number): void {
        this.checkAlloc(8);

        this.stream.InternalWriteByte(value & 0xFF); // Write lower byte
        this.stream.InternalWriteByte((value >> 8) & 0xFF);  // Write next byte
        this.stream.InternalWriteByte((value >> 16) & 0xFF);
        this.stream.InternalWriteByte((value >> 24) & 0xFF);

        // For values beyond 32 bits
        const high = Math.floor(value / 0x100000000);  // Get the upper 32 bits

        this.stream.InternalWriteByte(high & 0xFF);
        this.stream.InternalWriteByte((high >> 8) & 0xFF);
        this.stream.InternalWriteByte((high >> 16) & 0xFF);
        this.stream.InternalWriteByte((high >> 24) & 0xF);
    }

    writeInt64(value: number): void {
        this.checkAlloc(8);

        this.stream.InternalWriteByte(value & 0xFF); // Write lower byte
        this.stream.InternalWriteByte((value >> 8) & 0xFF);  // Write next byte
        this.stream.InternalWriteByte((value >> 16) & 0xFF);
        this.stream.InternalWriteByte((value >> 24) & 0xFF);

        // For values beyond 32 bits
        const high = Math.floor(value / 0x100000000);  // Get the upper 32 bits

        this.stream.InternalWriteByte(high & 0xFF);
        this.stream.InternalWriteByte((high >> 8) & 0xFF);
        this.stream.InternalWriteByte((high >> 16) & 0xFF);
        this.stream.InternalWriteByte((high >> 24) & 0xF);
    }

    writeFloat(value: number): void {
        this.checkAlloc(4);

        // Create a DataView to handle the conversion to IEEE 754
        const buffer = new ArrayBuffer(4);
        const view = new DataView(buffer);

        // Write the float value into the DataView in little-endian order
        view.setFloat32(0, value, true); // 'true' indicates little-endian
        // Convert ArrayBuffer to Uint8Array and write it to the stream
        this.stream.Write(new Uint8Array(buffer), 0, 4);
    }

    writeDouble(value: number): void {
        this.checkAlloc(8);

        const buffer = new ArrayBuffer(8);
        const view = new DataView(buffer);

        // Write the double value into the DataView in little-endian order
        view.setFloat64(0, value, true); // 'true' indicates little-endian

        // Convert ArrayBuffer to Uint8Array and write it to the stream
        this.stream.Write(new Uint8Array(buffer), 0, 8);
    }

    writeUint8Array(data: Uint8Array): void {
        this.checkAlloc(data.length);
        this.stream.Write(data, 0, data.length);
    }

    writeString(value: string): void {
        if (value === null || value === undefined) {
            // Write a length of 0 for null/undefined strings
            this.write7BitEncodedInt(0);
            return;
        }

        // Encode the string into a Uint8Array using UTF-8
        const encodedString = new TextEncoder().encode(value);
        const length = encodedString.length;

        // Write the length as a 7-bit encoded integer
        this.write7BitEncodedInt(length);

        // Write the encoded string bytes
        this.stream.Write(encodedString, 0, length);
    }

    write7BitEncodedInt(value: number): void {
        while (value >= 0x80) {
            this.writeByte((value & 0x7F) | 0x80); // Write 7 bits and set the continuation bit
            value >>= 7;
        }
        this.writeByte(value); // Write the last 7 bits without the continuation bit
    }

    getLength(): number {
        return this.stream.Length;
    }

    writeBoolean(bool: boolean) {
        this.writeByte(bool ? 1 : 0);
    }

    writeBool(bool: boolean) {
        this.writeByte(bool ? 1 : 0);
    }

    writeArray(array: Uint8Array) {
        this.writeInt32(array.length);
        this.writeUint8Array(array);
    }

    private checkAlloc(size: number): void {
        this.stream.InternalReserve(size);
    }
}
