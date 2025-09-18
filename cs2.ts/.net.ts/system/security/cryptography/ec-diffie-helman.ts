// @ts-nocheck
﻿import { IDisposable } from "../../disposable.interface";
import { ECCurve } from "./ec-curve";
import { ECDiffieHellmanPublicKey } from "./ec-diffie-helman-public-key";
import { ECParameters } from "./ec-parameters";
import { HashAlgorithmName } from "./hash-algorithm-name";
import { p256 } from '@noble/curves/p256';
import * as asn1 from 'asn1.js';

// Helper to convert HashAlgorithmName to Web Crypto API format
function toWebCryptoHashAlgorithm(hashAlgorithm: HashAlgorithmName): string {
    switch (hashAlgorithm) {
        case HashAlgorithmName.SHA1:
            return 'SHA-1';
        case HashAlgorithmName.SHA256:
            return 'SHA-256';
        case HashAlgorithmName.SHA384:
            return 'SHA-384';
        case HashAlgorithmName.SHA512:
            return 'SHA-512';
        default:
            throw new Error(`Unsupported hash algorithm: ${hashAlgorithm}`);
    }
}

export class ECDiffieHellman implements IDisposable {
    private _publicKey: ECDiffieHellmanPublicKey;
    private keyPair!: CryptoKeyPair;
    private curve!: ECCurve;

    public get publicKey(): ECDiffieHellmanPublicKey {
        return this._publicKey;
    }

    private constructor() {
        // leave uninitialized for ImportParameters
    }

    dispose(): void {
    }

    static async create(): Promise<ECDiffieHellman>;
    static async create(curve: ECCurve): Promise<ECDiffieHellman>;
    static async create(curve?: ECCurve): Promise<ECDiffieHellman> {
        const instance = new ECDiffieHellman();

        if (curve) {
            const keyPair = await crypto.subtle.generateKey(
                {
                    name: "ECDH",
                    namedCurve: "P-256",
                },
                true,
                ["deriveBits"]
            );
            instance.keyPair = keyPair;
            instance.curve = curve;

            const raw = new Uint8Array(await crypto.subtle.exportKey("raw", keyPair.publicKey));
            instance._publicKey = new ECDiffieHellmanPublicKey(raw, curve, keyPair.publicKey);
        }

        return instance;
    }

    async exportParameters(includePrivate: boolean): Promise<ECParameters> {
        if (!this.keyPair || !this.keyPair.publicKey) {
            throw new Error("Key pair is not initialized");
        }

        const raw = new Uint8Array(await crypto.subtle.exportKey("raw", this.keyPair.publicKey));
        if (raw[0] !== 0x04) {
            throw new Error("Unexpected public key format");
        }

        const coordinateLength = (raw.length - 1) / 2;
        const x = raw.slice(1, 1 + coordinateLength);
        const y = raw.slice(1 + coordinateLength);

        const result: ECParameters = {
            Q: { X: x, Y: y },
            Curve: this.curve
        };

        if (includePrivate) {
            const pkcs8 = new Uint8Array(await crypto.subtle.exportKey("pkcs8", this.keyPair.privateKey));
            result.D = extractPrivateKeyD(pkcs8);
        }

        return result;
    }


    toBase64Url(bytes: Uint8Array): string {
        const binary = String.fromCharCode(...bytes);
        return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    fromBase64Url(base64url: string): Uint8Array {
        const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/')
            + '==='.slice((base64url.length + 3) % 4);
        return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    }

    async importParameters(parameters: ECParameters): Promise<void> {
        let x: Uint8Array;
        let y: Uint8Array;

        if (!parameters.Q) {
            // Derive public key from private scalar using noble-curves
            const pub = p256.getPublicKey(parameters.D, false); // uncompressed format
            x = pub.slice(1, 33);
            y = pub.slice(33, 65);
        } else {
            x = parameters.Q.X;
            y = parameters.Q.Y;
        }

        const publicRaw = new Uint8Array(1 + x.length + y.length);
        publicRaw[0] = 0x04;
        publicRaw.set(x, 1);
        publicRaw.set(y, 1 + x.length);

        const publicKey = await crypto.subtle.importKey(
            "raw",
            publicRaw,
            { name: "ECDH", namedCurve: "P-256" },
            true,
            []
        );

        let privateKey: CryptoKey | undefined = undefined;

        if (parameters.D) {
            const jwk: JsonWebKey = {
                kty: "EC",
                crv: "P-256",
                x: this.toBase64Url(x),
                y: this.toBase64Url(y),
                d: toBase64Url(parameters.D),
                ext: true,
            };

            privateKey = await crypto.subtle.importKey(
                "jwk",
                jwk,
                { name: "ECDH", namedCurve: "P-256" },
                true,
                ["deriveBits"]
            );
        }

        const raw = new Uint8Array(await crypto.subtle.exportKey("raw", publicKey));
        this._publicKey = new ECDiffieHellmanPublicKey(raw, parameters.Curve, publicKey);

        this.keyPair = { publicKey, privateKey: privateKey! };
    }


    public async deriveKeyFromHash(
        otherPublicKey: ECDiffieHellmanPublicKey,
        hashAlgorithm: HashAlgorithmName,
        prependData?: Uint8Array,
        appendData?: Uint8Array
    ): Promise<Uint8Array> {
        const sharedSecret = await crypto.subtle.deriveBits(
            {
                name: "ECDH",
                public: otherPublicKey.key
            },
            this.keyPair.privateKey,
            256 // bits
        );

        // Apply prepend and append if needed (like C# API supports)
        let finalInput = new Uint8Array(sharedSecret);
        if (prependData || appendData) {
            const totalLength = (prependData?.length || 0) + finalInput.length + (appendData?.length || 0);
            const full = new Uint8Array(totalLength);
            let offset = 0;
            if (prependData) {
                full.set(prependData, offset);
                offset += prependData.length;
            }
            full.set(finalInput, offset);
            offset += finalInput.length;
            if (appendData) {
                full.set(appendData, offset);
            }
            finalInput = full;
        }

        const webCryptoHashAlgorithm = toWebCryptoHashAlgorithm(hashAlgorithm);
        const hash = await crypto.subtle.digest(webCryptoHashAlgorithm, finalInput);
        return new Uint8Array(hash);
    }
}

const ECPrivateKeyASN = asn1.define('ECPrivateKey', function (this: any) {
    this.seq().obj(
        this.key('version').int(),
        this.key('privateKey').octstr(),
        this.key('publicKey').optional().explicit(1).bitstr()
    );
});

const PrivateKeyInfoASN = asn1.define('PrivateKeyInfo', function (this: any) {
    this.seq().obj(
        this.key('version').int(),
        this.key('algorithm').seq().obj(
            this.key('algorithm').objid(),
            this.key('parameters').optional().any()
        ),
        this.key('privateKey').octstr()
    );
});


// Helper to extract 'D' (private key) from PKCS#8
function extractPrivateKeyD(pkcs8: Uint8Array): Uint8Array {
    const buffer = Buffer.from(pkcs8); // ✅ Convert to Buffer
    const decoded = PrivateKeyInfoASN.decode(buffer, 'der');
    const ecPrivate = ECPrivateKeyASN.decode(decoded.privateKey, 'der');
    return ecPrivate.privateKey;
}

// Base64url encode helper
function toBase64Url(bytes: Uint8Array): string {
    return Buffer.from(bytes)
        .toString("base64")
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=+$/, "");
}
