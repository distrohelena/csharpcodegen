// @ts-nocheck
import { ECCurve } from "./ec-curve";
import { ECPoint } from "./ec-point";

export class ECParameters {
    Q?: ECPoint;
    D?: Uint8Array;
    Curve: ECCurve;
}