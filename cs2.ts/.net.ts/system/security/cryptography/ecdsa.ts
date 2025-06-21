import { IDisposable } from "../../disposable.interface";
import { ECCurve } from "./ec-curve";
import { ECParameters } from "./ec-parameters";

export class ECDsa implements IDisposable {
    private keyPair!: CryptoKeyPair;
    private curve!: ECCurve;

    private constructor() {
        // Leave uninitialized for ImportParameters
    }

    dispose(): void {
        // Cleanup resources if necessary
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
        const publicRaw = new Uint8Array(1 + parameters.Q.X.length + parameters.Q.Y.length);
        publicRaw[0] = 0x04; // Uncompressed format
        publicRaw.set(parameters.Q.X, 1);
        publicRaw.set(parameters.Q.Y, 1 + parameters.Q.X.length);

        const publicKey = await crypto.subtle.importKey(
            "raw",
            publicRaw,
            { name: "ECDSA", namedCurve: "P-256" },
            true,
            []
        );

        let privateKey: CryptoKey | undefined = undefined;

        if (parameters.D) {
            // Import private key in JWK format (easier for EC than ASN.1)
            const jwk: JsonWebKey = {
                kty: "EC",
                crv: "P-256",
                x: toBase64Url(parameters.Q.X),
                y: toBase64Url(parameters.Q.Y),
                d: toBase64Url(parameters.D),
                ext: true,
            };

            privateKey = await crypto.subtle.importKey(
                "jwk",
                jwk,
                { name: "ECDSA", namedCurve: "P-256" },
                true,
                ["sign"]
            );
        }

        this.keyPair = { publicKey, privateKey: privateKey! };
    }

    // Sign a message using ECDSA
    async signHash(message: Uint8Array): Promise<Uint8Array> {
        if (!this.keyPair || !this.keyPair.privateKey) {
            throw new Error("Private key is not initialized");
        }

        const signature = await crypto.subtle.sign(
            { name: "ECDSA", hash: { name: "SHA-256" } },
            this.keyPair.privateKey,
            message
        );

        return new Uint8Array(signature);
    }

    // Verify a message signature using ECDSA
    async verifyHash(message: Uint8Array, signature: Uint8Array): Promise<boolean> {
        if (!this.keyPair || !this.keyPair.publicKey) {
            throw new Error("Public key is not initialized");
        }

        return await crypto.subtle.verify(
            { name: "ECDSA", hash: { name: "SHA-256" } },
            this.keyPair.publicKey,
            signature,
            message
        );
    }
}

// Helper to extract 'D' (private key) from PKCS#8
function extractPrivateKeyD(pkcs8: Uint8Array): Uint8Array {
    const dTag = 0x04; // OCTET STRING
    const index = pkcs8.lastIndexOf(dTag);
    if (index === -1 || index + 1 >= pkcs8.length) {
        throw new Error("Private key component 'D' not found");
    }

    const length = pkcs8[index + 1];
    return pkcs8.slice(index + 2, index + 2 + length);
}

// Base64url encode helper
function toBase64Url(bytes: Uint8Array): string {
    return Buffer.from(bytes)
        .toString("base64")
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=+$/, "");
}
