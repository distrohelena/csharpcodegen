// @ts-nocheck

let cachedCrypto: Crypto | null = null;

function tryGetGlobalCrypto(): Crypto | null {
    if (typeof globalThis !== "undefined" && globalThis.crypto) {
        return globalThis.crypto as Crypto;
    }
    return null;
}

function tryGetNodeCrypto(): Crypto | null {
    if (typeof process === "undefined" || !process.versions || !process.versions.node) {
        return null;
    }

    try {
        const maybeRequire = (Function("return typeof require !== 'undefined' ? require : undefined;") as () => any | undefined)();
        if (!maybeRequire) {
            return null;
        }

        const module = maybeRequire("node:crypto") ?? maybeRequire("crypto");
        if (module && module.webcrypto) {
            return module.webcrypto as Crypto;
        }
    } catch {
        // Ignore and fall through to error handling below.
    }

    return null;
}

export function getRuntimeCrypto(): Crypto {
    if (cachedCrypto) {
        return cachedCrypto;
    }

    const runtimeCrypto = tryGetGlobalCrypto() ?? tryGetNodeCrypto();
    if (!runtimeCrypto) {
        throw new Error("Web Crypto API is not available in this environment.");
    }

    cachedCrypto = runtimeCrypto;
    return runtimeCrypto;
}

export function getSubtleCrypto(): SubtleCrypto {
    return getRuntimeCrypto().subtle;
}

export function toArrayBuffer(view: Uint8Array): ArrayBuffer {
    return view.buffer.slice(view.byteOffset, view.byteOffset + view.byteLength);
}

export class WebCryptoUtil {
    static getRuntimeCrypto(): Crypto {
        return getRuntimeCrypto();
    }

    static getSubtleCrypto(): SubtleCrypto {
        return getSubtleCrypto();
    }

    static toArrayBuffer(view: Uint8Array): ArrayBuffer {
        return toArrayBuffer(view);
    }
}
