// @ts-nocheck
import { IEnumerable } from "./ienumerable";
import { IEqualityComparer } from "./iequalitycomparer";

/** Structural projection of System.Collections.Generic.ISet<T>. */
export interface ISet<T> extends IEnumerable<T> {
    readonly Count: number;
    readonly count: number;
    Add(item: T): boolean;
    Remove(item: T): boolean;
    Contains(item: T, comparer?: IEqualityComparer<T>): boolean;
}
