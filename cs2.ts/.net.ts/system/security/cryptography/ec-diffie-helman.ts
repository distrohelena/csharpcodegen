import { IDisposable } from "../../disposable.interface";
import { ECCurve } from "./ec-curve";
import { ECDiffieHellmanPublicKey } from "./ec-diffie-helman-public-key";
import { ECParameters } from "./ec-parameters";
import { HashAlgorithmName } from "./hash-algorithm-name";

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

    async importParameters(parameters: ECParameters): Promise<void> {
        const publicRaw = new Uint8Array(1 + parameters.Q.X.length + parameters.Q.Y.length);
        publicRaw[0] = 0x04; // Uncompressed format
        publicRaw.set(parameters.Q.X, 1);
        publicRaw.set(parameters.Q.Y, 1 + parameters.Q.X.length);

        const publicKey = await crypto.subtle.importKey(
            "raw",
            publicRaw,
            { name: "ECDH", namedCurve: "P-256" },
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
                { name: "ECDH", namedCurve: "P-256" },
                true,
                ["deriveBits"]
            );
        }

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

        const hash = await crypto.subtle.digest(hashAlgorithm, finalInput);
        return new Uint8Array(hash);
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
