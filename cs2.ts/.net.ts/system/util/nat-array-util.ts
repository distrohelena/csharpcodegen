// @ts-nocheck
import { ArgumentNullException } from "../argument-null.exception";
import { ArgumentException } from "../argument.exception";
import { List } from "../collections/generic/list";
import { Dictionary } from "../collections/generic/dictionary";
import { InvalidOperationException } from "../invalid-operation.exception";

export class Grouping<TKey, TValue> extends Array<TValue> {
    public readonly Key: TKey;

    constructor(key: TKey) {
        super();
        this.Key = key;
    }

    /** Returns an independent list-shaped snapshot, matching Enumerable.ToList materialization. */
    public toList(): TValue[] {
        return Array.from(this);
    }
}
export class NativeArrayUtil {
    /** Materializes any iterable into a detached mutable managed List. */
    static toList<T>(values: Iterable<T> | null | undefined): List<T> {
        if (values == null) throw new ArgumentNullException("source");
        return new List<T>(values);
    }

    /** Defers predicate filtering while preserving the source element order. */
    static where<T>(values: Iterable<T> | null | undefined,
        predicate: ((value: T, index: number) => boolean) | null | undefined): Iterable<T> {
        if (values == null) throw new ArgumentNullException("source");
        if (predicate == null) throw new ArgumentNullException("predicate");
        return { [Symbol.iterator]: () => NativeArrayUtil.whereIterator(values, predicate) };
    }

    /** Creates independent indexed filtering state for every enumeration. */
    private static *whereIterator<T>(values: Iterable<T>, predicate: (value: T, index: number) => boolean): IterableIterator<T> {
        let index = 0;
        for (const value of values) {
            if (predicate(value, index++)) {
                yield value;
            }
        }
    }

    /** Defers projection and supplies the same zero-based index as Enumerable.Select. */
    static select<T, TResult>(values: Iterable<T> | null | undefined,
        selector: ((value: T, index: number) => TResult) | null | undefined): Iterable<TResult> {
        if (values == null) throw new ArgumentNullException("source");
        if (selector == null) throw new ArgumentNullException("selector");
        return { [Symbol.iterator]: () => NativeArrayUtil.selectIterator(values, selector) };
    }

    /** Creates independent projection state for each enumeration of the returned sequence. */
    private static *selectIterator<T, TResult>(values: Iterable<T>,
        selector: (value: T, index: number) => TResult): IterableIterator<TResult> {
        let index = 0;
        for (const value of values) {
            yield selector(value, index++);
        }
    }

    /** Counts all values or only those accepted by the supplied predicate. */
    static count<T>(values: Iterable<T> | null | undefined,
        predicate?: ((value: T, index: number) => boolean) | null): number {
        if (values == null) throw new ArgumentNullException("source");
        if (predicate === null) throw new ArgumentNullException("predicate");
        let count = 0;
        let index = 0;
        for (const value of values) {
            if (!predicate || predicate(value, index)) count++;
            index++;
        }
        return count;
    }

    /** Checks the source eagerly while deferring bounded enumeration until the result is consumed. */
    static take<T>(values: Iterable<T> | null | undefined, count: number): Iterable<T> {
        if (values == null) throw new ArgumentNullException("source");
        return { [Symbol.iterator]: () => NativeArrayUtil.takeIterator(values, count) };
    }

    /** Yields at most count items and disposes the source without reading one excess item. */
    private static *takeIterator<T>(values: Iterable<T>, count: number): IterableIterator<T> {
        if (count <= 0) return;
        let remaining = count;
        for (const value of values) {
            yield value;
            if (--remaining <= 0) return;
        }
    }

    /** Defers distinct enumeration and preserves the first value in every comparer-defined group. */
    static distinct<T>(values: Iterable<T> | null | undefined,
        comparer?: { Equals?: (left: T, right: T) => boolean } | null): Iterable<T> {
        if (values == null) throw new ArgumentNullException("source");
        return { [Symbol.iterator]: () => NativeArrayUtil.distinctIterator(values, comparer) };
    }

    /** Builds independent equality state per enumeration so the result is safely reusable. */
    private static *distinctIterator<T>(values: Iterable<T>,
        comparer?: { Equals?: (left: T, right: T) => boolean } | null): IterableIterator<T> {
        const seen: T[] = [];
        for (const value of values) {
            if (!seen.some(previous => NativeArrayUtil.keysEqual(previous, value, comparer))) {
                seen.push(value);
                yield value;
            }
        }
    }

    /** Implements early-exit membership, retaining a collection's comparer only for the no-comparer overload. */
    static contains<T>(values: Iterable<T> | null | undefined, value: T,
        comparer?: { Equals?: (left: T, right: T) => boolean } | null): boolean {
        if (values == null) throw new ArgumentNullException("source");
        const collection = values as Iterable<T> & { Contains?: (item: T) => boolean; contains?: (item: T) => boolean };
        if (arguments.length === 2 && !Array.isArray(values)) {
            if (typeof collection.Contains === "function") return collection.Contains(value);
            if (typeof collection.contains === "function") return collection.contains(value);
        }
        for (const candidate of values) {
            if (NativeArrayUtil.keysEqual(candidate, value, comparer)) return true;
        }
        return false;
    }

    /** Mirrors Enumerable.GroupBy while preserving first-key order and an optional equality comparer. */
    static groupBy<T, TKey>(values: Iterable<T> | null | undefined, keySelector: ((value: T) => TKey) | null | undefined,
        comparer: { Equals?: (left: TKey, right: TKey) => boolean } | null | undefined): Grouping<TKey, T>[] {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        if (keySelector === null || keySelector === undefined) {
            throw new ArgumentNullException("keySelector");
        }
        const groups: Grouping<TKey, T>[] = [];
        for (const value of values) {
            const key = keySelector(value);
            let group = groups.find(candidate => NativeArrayUtil.keysEqual(candidate.Key, key, comparer));
            if (group === undefined) {
                group = new Grouping<TKey, T>(key);
                groups.push(group);
            }
            group.push(value);
        }
        return groups;
    }

    /** Mirrors Enumerable.ToDictionary including comparer-aware duplicate-key rejection. */
    static toDictionary<T, TKey>(values: Iterable<T> | null | undefined, keySelector: ((value: T) => TKey) | null | undefined,
        valueSelector: null | undefined, comparer: { Equals?: (left: TKey, right: TKey) => boolean; GetHashCode?: (value: TKey) => number } | null | undefined): Dictionary<TKey, T>;
    static toDictionary<T, TKey, TValue>(values: Iterable<T> | null | undefined, keySelector: ((value: T) => TKey) | null | undefined,
        valueSelector: ((value: T) => TValue), comparer: { Equals?: (left: TKey, right: TKey) => boolean; GetHashCode?: (value: TKey) => number } | null | undefined): Dictionary<TKey, TValue>;
    static toDictionary<T, TKey, TValue>(values: Iterable<T> | null | undefined, keySelector: ((value: T) => TKey) | null | undefined,
        valueSelector: ((value: T) => TValue) | null | undefined, comparer: { Equals?: (left: TKey, right: TKey) => boolean; GetHashCode?: (value: TKey) => number } | null | undefined): Dictionary<TKey, TValue | T> {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        if (keySelector === null || keySelector === undefined) {
            throw new ArgumentNullException("keySelector");
        }
        const result = new Dictionary<TKey, TValue | T>(comparer as any);
        for (const value of values) {
            const key = keySelector(value);
            if (result.containsKey(key)) {
                throw new ArgumentException("An item with the same key has already been added.", "key");
            }
            result.add(key, valueSelector ? valueSelector(value) : value);
        }
        return result;
    }

    private static keysEqual<TKey>(left: TKey, right: TKey, comparer: { Equals?: (left: TKey, right: TKey) => boolean } | null | undefined): boolean {
        if (comparer?.Equals) {
            return comparer.Equals(left, right);
        }
        return left === right || (typeof left === "number" && typeof right === "number" && Number.isNaN(left) && Number.isNaN(right));
    }
    /** Materializes an enumerable while preserving Enumerable.ToArray null checks. */
    static toArray<T>(values: Iterable<T> | null | undefined): T[] {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        return Array.from(values);
    }

    /** Mirrors Enumerable.Skip for the concrete browser iterable boundary. */
    static skip<T>(values: Iterable<T> | null | undefined, count: number): T[] {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        const skipped: T[] = [];
        let index = 0;
        for (const value of values) {
            if (index++ >= count) {
                skipped.push(value);
            }
        }
        return skipped;
    }

    /** Mirrors Enumerable.All including null source and predicate argument semantics. */
    static all<T>(values: Iterable<T> | null | undefined, predicate: ((value: T) => boolean) | null | undefined): boolean {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        if (predicate === null || predicate === undefined) {
            throw new ArgumentNullException("predicate");
        }
        for (const value of values) {
            if (!predicate(value)) {
                return false;
            }
        }
        return true;
    }

    /** Mirrors selector Enumerable.Sum for JavaScript's number-backed CLR numeric values. */
    static sum<T>(values: Iterable<T> | null | undefined, selector: ((value: T) => number) | null | undefined): number {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        if (selector === null || selector === undefined) {
            throw new ArgumentNullException("selector");
        }
        let total = 0;
        for (const value of values) {
            total += selector(value);
        }
        return total;
    }

    /** Mirrors comparer-aware Enumerable.OrderBy and evaluates each selector exactly once. */
    static orderBy<T, TKey>(values: Iterable<T> | null | undefined, selector: ((value: T) => TKey) | null | undefined,
        comparer?: { Compare?: (left: TKey, right: TKey) => number } | null): T[] {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        if (selector === null || selector === undefined) {
            throw new ArgumentNullException("keySelector");
        }
        const indexed = Array.from(values, (value, index) => ({ value, key: selector(value), index }));
        indexed.sort((left, right) => {
            let comparison: number;
            if (comparer?.Compare) {
                comparison = comparer.Compare(left.key, right.key);
            } else if (left.key === right.key) {
                comparison = 0;
            } else if (left.key == null) {
                comparison = -1;
            } else if (right.key == null) {
                comparison = 1;
            } else {
                comparison = left.key < right.key ? -1 : 1;
            }
            return comparison === 0 ? left.index - right.index : comparison;
        });
        return indexed.map(item => item.value);
    }
    /** Mirrors Enumerable.Single source and predicate overloads for arbitrary iterables. */
    static single<T>(values: Iterable<T> | null | undefined, predicate?: ((value: T) => boolean) | null): T {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        if (predicate === null) {
            throw new ArgumentNullException("predicate");
        }
        let found: T | undefined;
        let hasFound = false;
        for (const value of values) {
            if (predicate && !predicate(value)) {
                continue;
            }
            if (hasFound) {
                throw new InvalidOperationException(predicate ? "Sequence contains more than one matching element." : "Sequence contains more than one element.");
            }
            found = value;
            hasFound = true;
        }
        if (!hasFound) {
            throw new InvalidOperationException(predicate ? "Sequence contains no matching element." : "Sequence contains no elements.");
        }
        return found as T;
    }

    /** Copies byte ranges, including overlapping views, without relying on a Node Buffer global. */
    static blockCopy(source: ArrayBuffer | ArrayBufferView, sourceOffset: number,
        destination: ArrayBuffer | ArrayBufferView, destinationOffset: number, count: number): void {
        const sourceBytes = NativeArrayUtil.byteView(source);
        const destinationBytes = NativeArrayUtil.byteView(destination);
        if (!Number.isSafeInteger(sourceOffset) || !Number.isSafeInteger(destinationOffset) || !Number.isSafeInteger(count)
            || sourceOffset < 0 || destinationOffset < 0 || count < 0) {
            throw new RangeError("BlockCopy requires non-negative integer byte offsets and count.");
        }
        if (sourceOffset > sourceBytes.length - count || destinationOffset > destinationBytes.length - count) {
            throw new RangeError("BlockCopy byte range is outside the supplied arrays.");
        }
        destinationBytes.set(sourceBytes.subarray(sourceOffset, sourceOffset + count), destinationOffset);
    }

    /** Exposes the exact bytes of a primitive array view, preserving its offset and byte length. */
    private static byteView(value: ArrayBuffer | ArrayBufferView): Uint8Array {
        if (value instanceof ArrayBuffer) {
            return new Uint8Array(value);
        } else if (ArrayBuffer.isView(value)) {
            return new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
        }
        throw new TypeError("BlockCopy requires primitive array buffers or views.");
    }

    static copy(src: Uint8Array, dest: Uint8Array, length: number): void;
    static copy(src: Uint8Array, srcOffset: number, dest: Uint8Array, destOffset: number, length: number): void;
    static copy(src: Uint8Array, arg1: number | Uint8Array, arg2: Uint8Array | number, arg3?: number, arg4?: number): void {
        let srcOffset: number;
        let dest: Uint8Array;
        let destOffset: number;
        let length: number;

        if (arg1 instanceof Uint8Array) {
            srcOffset = 0;
            dest = arg1;
            destOffset = 0;
            length = typeof arg2 === "number" ? arg2 : dest.length;
        } else {
            srcOffset = (arg1 as number) ?? 0;
            if (!(arg2 instanceof Uint8Array)) {
                throw new TypeError("Destination array is required.");
            }
            dest = arg2;
            destOffset = arg3 ?? 0;
            length = arg4 ?? dest.length;
        }

        dest.set(src.subarray(srcOffset, srcOffset + length), destOffset);
    }

    /** Mirrors Enumerable.SingleOrDefault(source, predicate) for array-mapped reference sequences. */
    static singleOrDefault<T>(values: Iterable<T> | null | undefined, predicate: ((value: T) => boolean) | null | undefined): T | null {
        if (values === null || values === undefined) {
            throw new ArgumentNullException("source");
        }
        if (predicate === null || predicate === undefined) {
            throw new ArgumentNullException("predicate");
        }
        let found: T | null = null;
        let hasMatch = false;
        for (const value of values) {
            if (!predicate(value)) {
                continue;
            }
            if (hasMatch) {
                throw new InvalidOperationException("Sequence contains more than one matching element.");
            }
            found = value;
            hasMatch = true;
        }
        return found;
    }
    /** Mirrors Enumerable.SequenceEqual(first, second, comparer) for iterable collections. */
    static sequenceEqual<T>(first: Iterable<T> | null | undefined, second: Iterable<T> | null | undefined,
        comparer: { Equals(left: T, right: T): boolean } | null | undefined): boolean {
        if (first === null || first === undefined) {
            throw new ArgumentNullException("first");
        }
        if (second === null || second === undefined) {
            throw new ArgumentNullException("second");
        }
        if (comparer === null || comparer === undefined) {
            throw new ArgumentNullException("comparer");
        }
        const secondIterator = second[Symbol.iterator]();
        for (const firstValue of first) {
            const next = secondIterator.next();
            if (next.done || !comparer.Equals(firstValue, next.value)) {
                return false;
            }
        }
        return secondIterator.next().done === true;
    }
    /**
     * Constant-time comparison of two Uint8Arrays.
     * Returns true if they are equal in length and content.
     */
    static constantTimeSequenceEqual(a: Uint8Array, b: Uint8Array): boolean {
        if (a.length !== b.length) return false;

        let result = 0;
        for (let i = 0; i < a.length; i++) {
            result |= a[i] ^ b[i];
        }

        return result === 0;
    }
}


declare global {
    interface Uint8Array {
        AsSpan(start?: number, length?: number): Uint8Array;
        Clone(): Uint8Array;
    }
}

Uint8Array.prototype.AsSpan = function(start: number = 0, length?: number): Uint8Array {
    const end = length === undefined ? undefined : start + length;
    return this.subarray(start, end);
};

Uint8Array.prototype.Clone = function(): Uint8Array {
    return new Uint8Array(this);
};
