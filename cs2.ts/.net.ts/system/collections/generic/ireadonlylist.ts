// @ts-nocheck
import { IEnumerable } from "./ienumerable";

export interface IReadOnlyList<T> extends IEnumerable<T> {
    readonly length: number;
    readonly count?: number;
    readonly [index: number]: T;
}
