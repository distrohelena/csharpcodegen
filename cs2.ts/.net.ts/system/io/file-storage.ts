// @ts-nocheck
import { FileMode } from "./file-mode";

const storage = new Map<string, Uint8Array>();

export function fileExists(path: string): boolean {
    return storage.has(path);
}

export function readFileBuffer(path: string): Uint8Array {
    const buffer = storage.get(path);
    return buffer ? buffer.slice() : new Uint8Array(0);
}

export function writeFileBuffer(path: string, data: Uint8Array): void {
    storage.set(path, data.slice());
}

export function deleteFileBuffer(path: string): void {
    storage.delete(path);
}

export function ensureFileBuffer(path: string, mode: FileMode): void {
    const exists = storage.has(path);
    switch (mode) {
        case FileMode.CreateNew:
            if (exists) {
                throw new Error(`File already exists: ${path}`);
            }
            storage.set(path, new Uint8Array(0));
            break;
        case FileMode.Create:
        case FileMode.Truncate:
            storage.set(path, new Uint8Array(0));
            break;
        case FileMode.Open:
            if (!exists) {
                throw new Error(`File not found: ${path}`);
            }
            break;
        case FileMode.OpenOrCreate:
        case FileMode.Append:
            if (!exists) {
                storage.set(path, new Uint8Array(0));
            }
            break;
        default:
            if (!exists) {
                storage.set(path, new Uint8Array(0));
            }
            break;
    }
}
