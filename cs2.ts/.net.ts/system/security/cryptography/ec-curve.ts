// @ts-nocheck
import { ECCurveType } from "./ec-curve-type";
import { ECPoint } from "./ec-point";
import { HashAlgorithmName } from "./hash-algorithm-name";

// ECCurve structure in TypeScript
export class ECCurve {
    A?: Uint8Array;
    B?: Uint8Array;
    G: ECPoint;
    Order?: Uint8Array;
    Cofactor?: Uint8Array;
    Seed?: Uint8Array;
    CurveType: ECCurveType;
    Hash?: HashAlgorithmName;
    Polynomial?: Uint8Array;
    Prime?: Uint8Array;

    static NamedCurves: any = {
        nistP256: new ECCurve()
    }
}
