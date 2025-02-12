import { SeekOrigin } from "./seek-origin";

export abstract class Stream {
    abstract Read(buffer: Uint8Array, offset: number, count: number): number;
    abstract Write(buffer: Uint8Array, offset: number, count: number): void;
    abstract Seek(offset: number, origin: SeekOrigin): number;
    abstract SetLength(length: number): void;

    abstract get CanRead(): boolean;
    abstract get CanWrite(): boolean;
    abstract get CanSeek(): boolean;
    get CanTimeout(): boolean {
        return false;
    }

    abstract InternalReserve(count: number): void;
    abstract InternalWriteByte(byte: number): void;
    abstract InternalReadByte(): number;

    abstract get Length(): number;
    abstract get Position(): number;
    abstract set Position(value: number);

    get ReadTimeout(): number {
        throw new Error('Timeout not supposed');
    }
    set ReadTimeout(value: number) {
        throw new Error('Timeout not supposed');
    }

    get WriteTimeout(): number {
        throw new Error('Timeout not supposed');
    }
    set WriteTimeout(value: number) {
        throw new Error('Timeout not supposed');
    }

    Dispose() {
    }

    Close() {
    }

    Flush() {
    }
}
