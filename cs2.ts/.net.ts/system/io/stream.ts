import { IDisposable } from "../disposable.interface";
import { SeekOrigin } from "./seek-origin";

export abstract class Stream implements IDisposable {
    abstract read(buffer: Uint8Array, offset: number, count: number): number;
    abstract write(buffer: Uint8Array, offset: number, count: number): void;
    abstract seek(offset: number, origin: SeekOrigin): number;
    abstract setLength(length: number): void;

    abstract get canRead(): boolean;
    abstract get canWrite(): boolean;
    abstract get canSeek(): boolean;
    get canTimeout(): boolean {
        return false;
    }

    abstract internalReserve(count: number): void;
    abstract internalWriteByte(byte: number): void;
    abstract internalReadByte(): number;

    abstract get length(): number;
    abstract get position(): number;
    abstract set position(value: number);

    get readTimeout(): number {
        throw new Error('Timeout not supposed');
    }
    set readTimeout(value: number) {
        throw new Error('Timeout not supposed');
    }

    get writeTimeout(): number {
        throw new Error('Timeout not supposed');
    }
    set writeTimeout(value: number) {
        throw new Error('Timeout not supposed');
    }

    dispose() {
    }

    close() {
    }

    flush() {
    }
}
