// @ts-nocheck
import { IEnumerable } from "./ienumerable";

/** Structural projection of System.Collections.Generic.IReadOnlyCollection<T>. */
export interface IReadOnlyCollection<T> extends IEnumerable<T> {
    readonly Count: number;
    readonly count: number;
}
