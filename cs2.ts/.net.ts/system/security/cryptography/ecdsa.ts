import { IDisposable } from "../../disposable.interface";
import { ECCurve } from "./ec-curve";
import { ECParameters } from "./ec-parameters";
import { p256 } from "@noble/curves/p256";
import * as asn1 from "asn1.js";
import { cloneToArrayBuffer } from "./buffer-util";

export class ECDsa implements IDisposable {
    private keyPair!: CryptoKeyPair;
    private curve!: ECCurve;

    private constructor() {
        // Leave uninitialized for ImportParameters
    }

    dispose(): void {
        // Cleanup resources if necessary
    }

    static toBase64Url(bytes: Uint8Array): string {
        const binary = String.fromCharCode(...bytes);
        const base64 = Buffer.from(binary, "binary").toString("base64");
        return base64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    }

    static fromBase64Url(base64url: string): Uint8Array {
        const base64 = base64url.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((base64url.length + 3) % 4);
        return new Uint8Array(Buffer.from(base64, "base64"));
    }

    static async create(): Promise<ECDsa>;
    static async create(curve: ECCurve): Promise<ECDsa>;
    static async create(curve?: ECCurve): Promise<ECDsa> {
        const instance = new ECDsa();

        if (curve) {
            const keyPair = await crypto.subtle.generateKey(
                {
                    name: "ECDSA",
                    namedCurve: "P-256",
                },
                true,
                ["sign", "verify"]
            );
            instance.keyPair = keyPair;
            instance.curve = curve;
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

    async importParameters(parameters: ECParameters): Promise<void> {
        let publicKey: CryptoKey;
        let privateKey: CryptoKey | undefined = undefined;

        let x: Uint8Array;
        let y: Uint8Array;

        if (parameters.Q) {
            x = parameters.Q.X;
            y = parameters.Q.Y;
        } else {
            const point = p256.getPublicKey(parameters.D!, false);
            x = point.slice(1, 33);
            y = point.slice(33, 65);
        }

        const jwk: JsonWebKey = {
            kty: "EC",
            crv: "P-256",
            d: toBase64Url(parameters.D!),
            x: toBase64Url(x),
            y: toBase64Url(y),
            ext: true
        };

        privateKey = await crypto.subtle.importKey(
            "jwk",
            jwk,
            { name: "ECDSA", namedCurve: "P-256" },
            true,
            ["sign"]
        );

        const publicRaw = new Uint8Array(1 + x.length + y.length);
        publicRaw[0] = 0x04;
        publicRaw.set(x, 1);
        publicRaw.set(y, 1 + x.length);

        publicKey = await crypto.subtle.importKey(
            "raw",
            cloneToArrayBuffer(publicRaw),
            { name: "ECDSA", namedCurve: "P-256" },
            true,
            []
        );

        this.keyPair = { publicKey, privateKey: privateKey! };
    }

    async signHash(message: Uint8Array): Promise<Uint8Array> {
        if (!this.keyPair || !this.keyPair.privateKey) {
            throw new Error("Private key is not initialized");
        }

        const signature = await crypto.subtle.sign(
            { name: "ECDSA", hash: { name: "SHA-256" } },
            this.keyPair.privateKey,
            cloneToArrayBuffer(message)
        );

        return new Uint8Array(signature);
    }

    async verifyHash(message: Uint8Array, signature: Uint8Array): Promise<boolean> {
        if (!this.keyPair || !this.keyPair.publicKey) {
            throw new Error("Public key is not initialized");
        }

        return await crypto.subtle.verify(
            { name: "ECDSA", hash: { name: "SHA-256" } },
            this.keyPair.publicKey,
            cloneToArrayBuffer(signature),
            cloneToArrayBuffer(message)
        );
    }
}

const ECPrivateKeyASN = asn1.define("ECPrivateKey", function (this: any) {
    this.seq().obj(
        this.key("version").int(),
        this.key("privateKey").octstr(),
        this.key("publicKey").optional().explicit(1).bitstr()
    );
});

const PrivateKeyInfoASN = asn1.define("PrivateKeyInfo", function (this: any) {
    this.seq().obj(
        this.key("version").int(),
        this.key("algorithm").seq().obj(
            this.key("algorithm").objid(),
            this.key("parameters").optional().any()
        ),
        this.key("privateKey").octstr()
    );
});

function extractPrivateKeyD(pkcs8: Uint8Array): Uint8Array {
    const decoded = PrivateKeyInfoASN.decode(Buffer.from(pkcs8), "der");
    const ecPrivate = ECPrivateKeyASN.decode(decoded.privateKey, "der");
    return new Uint8Array(ecPrivate.privateKey);
}

function toBase64Url(bytes: Uint8Array): string {
    return Buffer.from(bytes)
        .toString("base64")
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=+$/, "");
}
