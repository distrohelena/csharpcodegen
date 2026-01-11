// @ts-nocheck
import { IDictionary } from "./dictionary.interface"
import { IEqualityComparer } from "./iequalitycomparer";
import { KeyValuePair } from "./key-value-pair";
import { List } from "./list";

export class Dictionary<TKey, TValue> implements IDictionary<TKey, TValue> {
    private buckets: { [key: string]: Array<{ key: TKey; value: TValue }> } = {};
    private keyToString?: (key: TKey) => string;
    private comparer?: IEqualityComparer<TKey>;
    private _count: number = 0;

    constructor();
    constructor(capacity: number);
    constructor(capacity: number, comparer: IEqualityComparer<TKey>);
    constructor(comparer: IEqualityComparer<TKey>);
    constructor(keyToString: (key: TKey) => string);
    constructor(entries: Iterable<[TKey, TValue]>);
    constructor(comparer: IEqualityComparer<TKey>, entries: Iterable<[TKey, TValue]>);
    constructor(entries: Iterable<[TKey, TValue]>, comparer: IEqualityComparer<TKey>);
    constructor(arg1?: any, arg2?: any) {
        let entries: Iterable<[TKey, TValue]> | undefined;

        if (typeof arg1 === "number") {
            // capacity is ignored in JS implementation
        } else if (typeof arg1 === "function") {
            this.keyToString = arg1;
        } else if (arg1 && typeof arg1 === "object" && typeof arg1.Equals === "function" && typeof arg1.GetHashCode === "function") {
            this.comparer = arg1 as IEqualityComparer<TKey>;
        } else if (arg1 && typeof arg1[Symbol.iterator] === "function") {
            entries = arg1 as Iterable<[TKey, TValue]>;
        }

        if (arg2) {
            if (typeof arg2 === "number") {
                // capacity is ignored in JS implementation
            } else if (typeof arg2 === "function") {
                this.keyToString = arg2;
            } else if (arg2 && typeof arg2 === "object" && typeof arg2.Equals === "function" && typeof arg2.GetHashCode === "function") {
                this.comparer = arg2 as IEqualityComparer<TKey>;
            } else if (arg2 && typeof arg2[Symbol.iterator] === "function") {
                entries = arg2 as Iterable<[TKey, TValue]>;
            }
        }

        if (entries) {
            for (const [key, value] of entries) {
                this.add(key, value);
            }
        }
    }

    private getHash(key: TKey): string {
        if (this.comparer) {
            return String(this.comparer.GetHashCode(key));
        }
        if (this.keyToString) {
            return this.keyToString(key);
        }
        return JSON.stringify(key);
    }

    private keysEqual(left: TKey, right: TKey): boolean {
        if (this.comparer) {
            return this.comparer.Equals(left, right);
        }
        return JSON.stringify(left) === JSON.stringify(right);
    }

    // Add a key-value pair to the dictionary
    public add(key: TKey, value: TValue): void {
        const hash = this.getHash(key);
        const bucket = this.buckets[hash] ?? [];
        for (let i = 0; i < bucket.length; i++) {
            if (this.keysEqual(bucket[i].key, key)) {
                throw new Error("Key already exists in dictionary.");
            }
        }
        bucket.push({ key, value });
        this.buckets[hash] = bucket;
        this._count++;
    }

    // Try to add a key-value pair without throwing if it already exists
    public TryAdd(key: TKey, value: TValue): boolean {
        const hash = this.getHash(key);
        const bucket = this.buckets[hash] ?? [];
        for (let i = 0; i < bucket.length; i++) {
            if (this.keysEqual(bucket[i].key, key)) {
                return false;
            }
        }
        bucket.push({ key, value });
        this.buckets[hash] = bucket;
        this._count++;
        return true;
    }

    // Try to get the value by key, returning a boolean for success/failure
    public tryGetValue(key: TKey, outValue: { value?: TValue }): boolean {
        const hash = this.getHash(key);
        const bucket = this.buckets[hash];
        if (!bucket) {
            outValue.value = undefined;
            return false;
        }
        for (let i = 0; i < bucket.length; i++) {
            if (this.keysEqual(bucket[i].key, key)) {
                outValue.value = bucket[i].value;
                return true;
            }
        }
        outValue.value = undefined;
        return false;
    }

    // Set value by key using indexing
    public set(key: TKey, value: TValue): void {
        this.add(key, value);
    }

    // Get the value by key
    public get(key: TKey): TValue | undefined {
        const out = { value: undefined as TValue | undefined };
        return this.tryGetValue(key, out) ? out.value : undefined;
    }

    // Remove an item by key
    public remove(key: TKey): boolean {
        const hash = this.getHash(key);
        const bucket = this.buckets[hash];
        if (!bucket) {
            return false;
        }
        for (let i = 0; i < bucket.length; i++) {
            if (this.keysEqual(bucket[i].key, key)) {
                bucket.splice(i, 1);
                if (bucket.length === 0) {
                    delete this.buckets[hash];
                } else {
                    this.buckets[hash] = bucket;
                }
                this._count--;
                return true;
            }
        }
        return false;
    }

    // Check if a key exists
    public containsKey(key: TKey): boolean {
        const out = { value: undefined as TValue | undefined };
        return this.tryGetValue(key, out);
    }

    // Get the count of elements in the dictionary
    public get count(): number {
        return this._count;
    }

    // Uppercase aliases for generated code that preserves .NET naming
    public get Count(): number {
        return this._count;
    }

    // Get all keys in the dictionary
    public get keys(): TKey[] {
        const result: TKey[] = [];
        for (const hash of Object.keys(this.buckets)) {
            const bucket = this.buckets[hash];
            for (let i = 0; i < bucket.length; i++) {
                result.push(bucket[i].key);
            }
        }
        return result;
    }

    // Uppercase alias for .NET naming
    public get Keys(): TKey[] {
        return this.keys;
    }

    // Get all values in the dictionary
    public get values(): TValue[] {
        const result: TValue[] = [];
        for (const hash of Object.keys(this.buckets)) {
            const bucket = this.buckets[hash];
            for (let i = 0; i < bucket.length; i++) {
                result.push(bucket[i].value);
            }
        }
        return result;
    }

    // Uppercase alias for .NET naming
    public get Values(): TValue[] {
        return this.values;
    }

    // Clear the dictionary
    public clear(): void {
        this.buckets = {};
        this._count = 0;
    }

    // Iterate over the dictionary using a callback function
    public forEach(callback: (key: TKey, value: TValue) => void): void {
        for (const hash of Object.keys(this.buckets)) {
            const bucket = this.buckets[hash];
            for (let i = 0; i < bucket.length; i++) {
                callback(bucket[i].key, bucket[i].value);
            }
        }
    }

    public orderBy(
        selector: (pair: KeyValuePair<TKey, TValue>) => any
    ): List<KeyValuePair<TKey, TValue>> {
        const result: Array<KeyValuePair<TKey, TValue>> = [];

        this.forEach((key, value) => {
            result.push({ Key: key, Value: value });
        });

        const list = new List<KeyValuePair<TKey, TValue>>();

        list.addRange(result.sort((a, b) => {
            const aKey = selector(a);
            const bKey = selector(b);
            if (aKey < bKey) return -1;
            if (aKey > bKey) return 1;
            return 0;
        }));

        return list;
    }

    [Symbol.iterator](): Iterator<KeyValuePair<TKey, TValue>> {
        const entries: KeyValuePair<TKey, TValue>[] = [];

        this.forEach((key, value) => {
            entries.push({ Key: key, Value: value });
        });

        let index = 0;

        return {
            next(): IteratorResult<KeyValuePair<TKey, TValue>> {
                if (index < entries.length) {
                    return { value: entries[index++], done: false };
                }
                return { value: undefined as any, done: true };
            }
        };
    }
}
