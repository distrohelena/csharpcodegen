import { IDictionary } from "../generic/dictionary.interface";
import { KeyValuePair } from "../generic/key-value-pair";
import { List } from "../generic/list";

export class ConcurrentDictionary<TKey, TValue> implements IDictionary<TKey, TValue> {
    private items: { [key: string]: TValue } = {};
    private keyToString: (key: TKey) => string;
    private _count: number = 0;

    // The browser implementation is single-threaded, but accepts the CLR comparer/concurrency
    // constructor shapes so shared source keeps its dictionary semantics at the API boundary.
    constructor(comparerOrConcurrency?: unknown, capacity?: number) {
        this.keyToString = ((key: TKey) => JSON.stringify(key));
    }

    // Add a key-value pair to the dictionary
    public add(key: TKey, value: TValue): void {
        const stringKey = this.keyToString(key);
        if (this.items.hasOwnProperty(stringKey)) {
            throw new Error("Key already exists in dictionary.");
        }
        this.items[stringKey] = value;
        this._count++;
    }

    // Try to get the value by key, returning a boolean for success/failure
    public tryGetValue(key: TKey, outValue: { value?: TValue }): boolean {
        const stringKey = this.keyToString(key);
        if (this.items.hasOwnProperty(stringKey)) {
            outValue.value = this.items[stringKey];
            return true;
        }
        outValue.value = undefined;
        return false;
    }

    // Set value by key using indexing
    public set(key: TKey, value: TValue): void {
        const stringKey = this.keyToString(key);
        if (!Object.prototype.hasOwnProperty.call(this.items, stringKey)) {
            this._count++;
        }
        this.items[stringKey] = value;
    }

    /** Mirrors ConcurrentDictionary.TryAdd without replacing an existing value. */
    public TryAdd(key: TKey, value: TValue): boolean {
        if (this.containsKey(key)) {
            return false;
        }
        this.set(key, value);
        return true;
    }

    /** Mirrors Dictionary.GetValueOrDefault for the concrete browser map. */
    public GetValueOrDefault(key: TKey, defaultValue: TValue): TValue {
        const value = this.get(key);
        return value === undefined ? defaultValue : value;
    }

    /** Mirrors ConcurrentDictionary.TryRemove and assigns the removed value only on success. */
    public TryRemove(key: TKey, outValue: { value?: TValue }): boolean {
        return this.remove(key, outValue);
    }

    /** Produces detached key/value snapshots so mutation while iterating is safe. */
    public ToArray(): KeyValuePair<TKey, TValue>[] {
        const snapshot: KeyValuePair<TKey, TValue>[] = [];
        this.forEach((key, value) => snapshot.push({ Key: key, Value: value }));
        return snapshot;
    }

    public [Symbol.iterator](): Iterator<KeyValuePair<TKey, TValue>> {
        return this.ToArray()[Symbol.iterator]();
    }

    // Get the value by key
    public get(key: TKey): TValue | undefined {
        const stringKey = this.keyToString(key);
        return this.items.hasOwnProperty(stringKey) ? this.items[stringKey] : undefined;
    }

    // Remove an item by key
    public remove(key: TKey, outValue?: { value?: TValue }): boolean {
        const stringKey = this.keyToString(key);
        if (this.items.hasOwnProperty(stringKey)) {
            if (outValue) {
                outValue.value = this.items[stringKey];
            }
            delete this.items[stringKey];
            this._count--;
            return true;
        }
        if (outValue) {
            outValue.value = undefined;
        }
        return false;
    }

    /** Implements the managed dictionary removal member without changing TryRemove out-value semantics. */
    public Remove(key: TKey): boolean { return this.remove(key); }

    // Check if a key exists
    public containsKey(key: TKey): boolean {
        const stringKey = this.keyToString(key);
        return this.items.hasOwnProperty(stringKey);
    }

    public GetOrAdd(key: TKey, valueFactory: ((key: TKey) => TValue) | TValue): TValue {
        const stringKey = this.keyToString(key);
        if (this.items.hasOwnProperty(stringKey)) {
            return this.items[stringKey];
        }

        const value = typeof valueFactory === "function"
            ? (valueFactory as (key: TKey) => TValue)(key)
            : valueFactory;

        this.items[stringKey] = value;
        this._count++;
        return value;
    }

    // Get the count of elements in the dictionary
    public get count(): number {
        return this._count;
    }

    public get Count(): number {
        return this._count;
    }

    // Get all keys in the dictionary
    public get keys(): TKey[] {
        return Object.keys(this.items).map(key => JSON.parse(key));
    }

    public get Keys(): TKey[] {
        return this.keys;
    }

    // Get all values in the dictionary
    public get values(): TValue[] {
        return Object.values(this.items);
    }

    public get Values(): TValue[] {
        return this.values;
    }

    // Clear the dictionary
    public clear(): void {
        this.items = {};
        this._count = 0;
    }

    // Iterate over the dictionary using a callback function
    public forEach(callback: (key: TKey, value: TValue) => void): void {
        for (const key in this.items) {
            if (this.items.hasOwnProperty(key)) {
                callback(JSON.parse(key), this.items[key]);
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

}
