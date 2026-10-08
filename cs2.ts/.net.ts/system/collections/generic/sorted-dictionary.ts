// @ts-nocheck
import { KeyValuePair } from "./key-value-pair";
import { List } from "./list";

type OrderingComparer<TKey> = { Compare(left: TKey, right: TKey): number };

/**
 * Ordered dictionary used where C# canonicalizes signed payloads through
 * SortedDictionary<TKey, TValue>. Iteration and JSON projection preserve comparer order.
 */
export class SortedDictionary<TKey, TValue> implements Iterable<KeyValuePair<TKey, TValue>> {
    private readonly entries: Array<KeyValuePair<TKey, TValue>> = [];

    public constructor(private readonly comparer?: OrderingComparer<TKey>) {
    }

    /** Converter constructor factory for new SortedDictionary<TKey, TValue>(comparer). */
    public static New2<TKey, TValue>(comparer: OrderingComparer<TKey>): SortedDictionary<TKey, TValue> {
        return new SortedDictionary<TKey, TValue>(comparer);
    }

    public get Count(): number {
        return this.entries.length;
    }

    public get count(): number {
        return this.entries.length;
    }

    public set(key: TKey, value: TValue): void {
        const index = this.findIndex(key);
        if (index >= 0) {
            this.entries[index].Value = value;
            return;
        }

        this.entries.push(new KeyValuePair(key, value));
        this.entries.sort((left, right) => this.compare(left.Key, right.Key));
    }

    public get(key: TKey): TValue | undefined {
        const index = this.findIndex(key);
        return index < 0 ? undefined : this.entries[index].Value;
    }

    public ContainsKey(key: TKey): boolean {
        return this.findIndex(key) >= 0;
    }

    public tryGetValue(key: TKey, outValue: { value?: TValue }): boolean {
        const index = this.findIndex(key);
        if (index < 0) {
            outValue.value = undefined;
            return false;
        }
        outValue.value = this.entries[index].Value;
        return true;
    }

    public get Keys(): List<TKey> {
        return new List<TKey>(this.entries.map(entry => entry.Key));
    }

    public get Values(): List<TValue> {
        return new List<TValue>(this.entries.map(entry => entry.Value));
    }

    public [Symbol.iterator](): Iterator<KeyValuePair<TKey, TValue>> {
        return this.entries[Symbol.iterator]();
    }

    /** Lets JSON.stringify retain the deterministic key ordering used for canonical payload hashes. */
    public toJSON(): Record<string, TValue> {
        const result: Record<string, TValue> = {};
        for (const entry of this.entries) {
            result[String(entry.Key)] = entry.Value;
        }
        return result;
    }

    private findIndex(key: TKey): number {
        return this.entries.findIndex(entry => this.compare(entry.Key, key) === 0);
    }

    private compare(left: TKey, right: TKey): number {
        if (this.comparer) {
            return this.comparer.Compare(left, right);
        }
        if (left === right) {
            return 0;
        }
        return left == null ? -1 : right == null ? 1 : left < right ? -1 : 1;
    }
}
