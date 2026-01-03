// @ts-nocheck
import { List } from "./list";

export interface IEnumerable<T> {
    GetEnumerator?(): Iterator<T>;
    [Symbol.iterator](): Iterator<T>;
    toList(): List<T>;
    Any(predicate?: (item: T) => boolean): boolean;
    Where(predicate: (item: T, index: number) => boolean): T[];
    ToArray(): T[];
}
