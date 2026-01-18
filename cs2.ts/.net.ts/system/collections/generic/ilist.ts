// @ts-nocheck
import { ICollection } from "./icollection";

export interface IList<T> extends ICollection<T> {
    readonly count: number;
    readonly Count: number;
    [index: number]: T;
    indexOf(item: T): number;
    insert(index: number, item: T): void;
    removeAt(index: number): void;
}
