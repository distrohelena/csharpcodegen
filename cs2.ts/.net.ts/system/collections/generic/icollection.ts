// @ts-nocheck
import { IEnumerable } from "./ienumerable";

/** Mutable generic collection retains the CLR enumerable contract. */
export interface ICollection<T> extends IEnumerable<T> {
    readonly Count: number;
    readonly count: number;
    add(item: T): void;
    clear(): void;
    contains(item: T, comparer?: { Equals(left: T, right: T): boolean }): boolean;
    remove(item: T): boolean;
    toArray(): T[];
}
