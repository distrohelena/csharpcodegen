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
    export function readFileSync(path: string, options?: any): string | Buffer;
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
    export const Buffer: any;
}

declare module "node:buffer" {
    export const Buffer: any;
}
