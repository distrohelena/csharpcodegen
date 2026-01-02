// @ts-nocheck
export interface IEnumerable<T> {
    GetEnumerator?(): Iterator<T>;
    [Symbol.iterator](): Iterator<T>;
}
