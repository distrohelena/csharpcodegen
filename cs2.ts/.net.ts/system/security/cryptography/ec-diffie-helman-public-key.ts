import { IDisposable } from "../../disposable.interface";
import { ECCurve } from "./ec-curve";
import { ECParameters } from "./ec-parameters";

export class ECDiffieHellmanPublicKey implements IDisposable {
    private _rawKey: Uint8Array;
    private _curve: ECCurve;

    constructor(rawKey: Uint8Array, curve: ECCurve) {
        this._rawKey = rawKey;
        this._curve = curve;
    }

    dispose(): void {
    }

    async exportParameters(): Promise<ECParameters> {
        if (this._rawKey[0] !== 0x04) {
            throw new Error("Unexpected public key format");
        }

        const coordinateLength = (this._rawKey.length - 1) / 2;
        const x = this._rawKey.slice(1, 1 + coordinateLength);
        const y = this._rawKey.slice(1 + coordinateLength);

        const result: ECParameters = {
            Q: { X: x, Y: y },
            Curve: this._curve
        };

        return result;
    }
}
