// @ts-nocheck
import { Stream } from "./stream";
import { SeekOrigin } from "./seek-origin";
import * as fs from "fs";
import { FileMode } from "./file-mode";
import { FileAccess } from "./file-access";
import { FileShare } from "./file-share";

export class FileStream extends Stream {
    private fd: number;
    private _position: number = 0;

    constructor(path: string, mode: FileMode = FileMode.Open, access: FileAccess = FileAccess.ReadWrite, share: FileShare = FileShare.None) {
        super();
        let flag: string;

        const canRead = access === FileAccess.Read || access === FileAccess.ReadWrite;
        const canWrite = access === FileAccess.Write || access === FileAccess.ReadWrite;

        switch (mode) {
            case FileMode.Append:
                flag = "a+";
                break;
            case FileMode.Create:
                flag = canRead ? "w+" : "w";
                break;
            case FileMode.CreateNew:
                flag = canRead ? "wx+" : "wx";
                break;
            case FileMode.Open:
                flag = canWrite ? "r+" : "r";
                break;
            case FileMode.OpenOrCreate:
                flag = canWrite ? "r+" : "r";
                break;
            case FileMode.Truncate:
                flag = canRead ? "w+" : "w";
                break;
            default:
                throw new Error("Invalid FileMode");
        }

        this.fd = fs.openSync(path, flag);
        this._position = 0;
    }

    read(buffer: Uint8Array, offset: number, count: number): number {
        const tempBuffer = new Uint8Array(count);
        const bytesRead = fs.readSync(this.fd, tempBuffer, 0, count, this._position);
        buffer.set(tempBuffer.subarray(0, bytesRead), offset);
        this._position += bytesRead;
        return bytesRead;
    }

    write(buffer: Uint8Array, offset: number, count: number): void {
        const tempBuffer = buffer.subarray(offset, offset + count);
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
                this._position = this.length + offset;
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
        // No-op for file streams.
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
        if (this.fd !== undefined && this.fd >= 0) {
            fs.closeSync(this.fd);
            this.fd = -1;
        }
    }

    flush(): void {
        if (this.fd !== undefined && this.fd >= 0) {
            fs.fsyncSync(this.fd);
        }
    }
}
