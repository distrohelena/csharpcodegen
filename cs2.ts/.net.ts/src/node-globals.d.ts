// @ts-nocheck
declare module "crypto" {
    export function createHash(algorithm: string): { update(data: any): any; digest(encoding?: string): any };
    export function createHmac(algorithm: string, key: any): { update(data: any): any; digest(encoding?: string): any };
    export function randomBytes(size: number): Buffer;
    export function randomFillSync<T extends ArrayBufferView>(buffer: T, offset?: number, length?: number, position?: number): T;
    export function pbkdf2Sync(password: any, salt: any, iterations: number, keylen: number, digest: string): Buffer;
    export const webcrypto: any;
}

declare module "fs" {
    export function readdirSync(path: string): string[];
    export function statSync(path: string): { isDirectory(): boolean; isFile(): boolean };
    /** An explicit text encoding makes Node decode the file and return text. */
    export function readFileSync(path: string, options: string | { encoding: string; flag?: string }): string;
    /** Without a text encoding Node returns the original file bytes. */
    export function readFileSync(path: string, options?: { encoding?: null; flag?: string }): Buffer;
    /** A dynamic encoding retains both possible return types until the caller narrows it. */
    export function readFileSync(path: string, options: { encoding?: string | null; flag?: string } | string | null): string | Buffer;
    export function writeFileSync(path: string, data: any): void;
    export function mkdirSync(path: string, options?: any): void;
    export function existsSync(path: string): boolean;
    export function openSync(path: string, flags: string | number, mode?: number): number;
    export function closeSync(fd: number): void;
    export function readSync(fd: number, buffer: Buffer, offset: number, length: number, position: number): number;
    export function writeSync(fd: number, buffer: Buffer, offset: number, length: number, position?: number): number;
}

declare module "path" {
    export function join(...paths: string[]): string;
    export function resolve(...paths: string[]): string;
    export function dirname(path: string): string;
    export function basename(path: string): string;
    export function extname(path: string): string;
}

declare module "@noble/curves/p256" {
    export const p256: any;
}

declare module "node:crypto" {
    export * from "crypto";
}

declare module "node:fs" {
    export * from "fs";
}

declare module "node:path" {
    export * from "path";
}

declare module "asn1.js" {
    const asn1: any;
    export = asn1;
}

declare const Buffer: any;

declare module "buffer" {
    /** Browser-buffer operations used by generated and handwritten binary code. */
    export class Buffer extends Uint8Array {
        public static alloc(size: number): Buffer;
        public static from(data: string | ArrayBuffer | ArrayLike<number>, encoding?: string): Buffer;
        public write(string: string, offset?: number, length?: number, encoding?: string): number;
        public equals(otherBuffer: Uint8Array): boolean;
        public copy(targetBuffer: Buffer, targetStart?: number, sourceStart?: number, sourceEnd?: number): number;
    }
}

declare module "node:buffer" {
    export { Buffer } from "buffer";
}
