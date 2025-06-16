import { Stream } from "./stream";
import { SeekOrigin } from "./seek-origin";
import * as fs from "fs";
import { FileMode } from "./file-mode";

export class FileStream extends Stream {
    private fd: number;
    private _position: number = 0;

    constructor(path: string, mode: FileMode) {
        super();
        let flag: string;
        switch (mode) {
            case FileMode.Append:
                flag = "a+";
                break;
            case FileMode.Create:
                flag = "w+";
                break;
            case FileMode.CreateNew:
                flag = "wx+";
                break;
            case FileMode.Open:
                flag = "r";
                break;
            case FileMode.OpenOrCreate:
                flag = "r+";
                break;
            case FileMode.Truncate:
                flag = "w";
                break;
            default:
                throw new Error("Invalid FileMode");
        }
        this.fd = fs.openSync(path, flag);
        this._position = 0;
    }

    read(buffer: Uint8Array, offset: number, count: number): number {
        const tempBuffer = Buffer.alloc(count);
        const bytesRead = fs.readSync(this.fd, tempBuffer, 0, count, this._position);
        buffer.set(tempBuffer.subarray(0, bytesRead), offset);
        this._position += bytesRead;
        return bytesRead;
    }

    write(buffer: Uint8Array, offset: number, count: number): void {
        const tempBuffer = Buffer.from(buffer.subarray(offset, offset + count));
        const bytesWritten = fs.writeSync(this.fd, tempBuffer, 0, count, this._position);
        this._position += bytesWritten;
    }

    seek(offset: number, origin: SeekOrigin): number {
        switch (origin) {
            case SeekOrigin.Begin:
                this._position = offset;
                break;
            case SeekOrigin.Current:
                this._position += offset;
                break;
            case SeekOrigin.End:
                this._position = this.Length + offset;
                break;
        }
        return this._position;
    }

    setLength(length: number): void {
        fs.ftruncateSync(this.fd, length);
    }

    get canRead(): boolean {
        return true;
    }

    get canWrite(): boolean {
        return true;
    }

    get canSeek(): boolean {
        return true;
    }

    internalReserve(count: number): void {
        // Not needed for file streams
    }

    internalWriteByte(byte: number): void {
        const buffer = new Uint8Array([byte]);
        this.write(buffer, 0, 1);
    }

    internalReadByte(): number {
        const buffer = new Uint8Array(1);
        const bytesRead = this.read(buffer, 0, 1);
        return bytesRead > 0 ? buffer[0] : -1;
    }

    get length(): number {
        return fs.fstatSync(this.fd).size;
    }

    get position(): number {
        return this._position;
    }
    set position(value: number) {
        this._position = value;
    }

    dispose(): void {
        this.close();
    }

    close(): void {
        if (this.fd !== undefined) {
            fs.closeSync(this.fd);
            this.fd = -1;
        }
    }

    flush(): void {
        fs.fsyncSync(this.fd);
    }
}
