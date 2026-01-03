// @ts-nocheck
import { IEqualityComparer } from "./iequalitycomparer";

export class HashSet<T> {
    private buckets: { [key: string]: T[] } = {};
    private keyToString?: (key: T) => string;
    private comparer?: IEqualityComparer<T>;
    private _count: number = 0;

    constructor();
    constructor(capacity: number);
    constructor(capacity: number, comparer: IEqualityComparer<T>);
    constructor(comparer: IEqualityComparer<T>);
    constructor(entries: Iterable<T>);
    constructor(entries: Iterable<T>, comparer: IEqualityComparer<T>);
    constructor(comparer: IEqualityComparer<T>, entries: Iterable<T>);
    constructor(keyToString: (key: T) => string);
    constructor(entries: Iterable<T>, keyToString: (key: T) => string);
    constructor(arg1?: any, arg2?: any) {
        let entries: Iterable<T> | undefined;

        if (typeof arg1 === "number") {
            // capacity is ignored in JS implementation
        } else if (typeof arg1 === "function") {
            this.keyToString = arg1;
        } else if (arg1 && typeof arg1 === "object" && typeof arg1.Equals === "function" && typeof arg1.GetHashCode === "function") {
            this.comparer = arg1 as IEqualityComparer<T>;
        } else if (arg1 && typeof arg1[Symbol.iterator] === "function") {
            entries = arg1 as Iterable<T>;
        }

        if (arg2) {
            if (typeof arg2 === "number") {
                // capacity is ignored in JS implementation
            } else if (typeof arg2 === "function") {
                this.keyToString = arg2;
            } else if (arg2 && typeof arg2 === "object" && typeof arg2.Equals === "function" && typeof arg2.GetHashCode === "function") {
                this.comparer = arg2 as IEqualityComparer<T>;
            } else if (arg2 && typeof arg2[Symbol.iterator] === "function") {
                entries = arg2 as Iterable<T>;
            }
        }

        if (entries) {
            for (const item of entries) {
                this.Add(item);
            }
        }
    }

    private getHash(item: T): string {
        if (this.comparer) {
            return String(this.comparer.GetHashCode(item));
        }
        if (this.keyToString) {
            return this.keyToString(item);
        }
        return JSON.stringify(item);
    }

    private itemsEqual(left: T, right: T): boolean {
        if (this.comparer) {
            return this.comparer.Equals(left, right);
        }
        return JSON.stringify(left) === JSON.stringify(right);
    }

    private createComparableSet(items: Iterable<T>): HashSet<T> {
        if (items === this) {
            return this;
        }
        if (this.comparer) {
            return new HashSet<T>(items, this.comparer);
        }
        if (this.keyToString) {
            return new HashSet<T>(items, this.keyToString);
        }
        return new HashSet<T>(items);
    }

    // Add an item to the set, returning false if it already exists
    public Add(item: T): boolean {
        const hash = this.getHash(item);
        const bucket = this.buckets[hash] ?? [];
        for (let i = 0; i < bucket.length; i++) {
            if (this.itemsEqual(bucket[i], item)) {
                return false;
            }
        }
        bucket.push(item);
        this.buckets[hash] = bucket;
        this._count++;
        return true;
    }

    public add(item: T): boolean {
        return this.Add(item);
    }

    // Remove an item from the set
    public Remove(item: T): boolean {
        const hash = this.getHash(item);
        const bucket = this.buckets[hash];
        if (!bucket) {
            return false;
        }
        for (let i = 0; i < bucket.length; i++) {
            if (this.itemsEqual(bucket[i], item)) {
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

    public remove(item: T): boolean {
        return this.Remove(item);
    }

    // Check if an item exists in the set
    public Contains(item: T): boolean {
        const hash = this.getHash(item);
        const bucket = this.buckets[hash];
        if (!bucket) {
            return false;
        }
        for (let i = 0; i < bucket.length; i++) {
            if (this.itemsEqual(bucket[i], item)) {
                return true;
            }
        }
        return false;
    }

    public contains(item: T): boolean {
        return this.Contains(item);
    }

    // Clear the set
    public Clear(): void {
        this.buckets = {};
        this._count = 0;
    }

    public clear(): void {
        this.Clear();
    }

    // Get the number of items in the set
    public get Count(): number {
        return this._count;
    }

    public get count(): number {
        return this._count;
    }

    // Return the set contents as an array
    public ToArray(): T[] {
        return this.toArray();
    }

    public toArray(): T[] {
        const result: T[] = [];
        for (const hash of Object.keys(this.buckets)) {
            const bucket = this.buckets[hash];
            for (let i = 0; i < bucket.length; i++) {
                result.push(bucket[i]);
            }
        }
        return result;
    }

    public UnionWith(items: Iterable<T>): void {
        if (!items) {
            throw new Error("Items cannot be null.");
        }
        for (const item of items) {
            this.Add(item);
        }
    }

    public ExceptWith(items: Iterable<T>): void {
        if (!items) {
            throw new Error("Items cannot be null.");
        }
        for (const item of items) {
            this.Remove(item);
        }
    }

    public IntersectWith(items: Iterable<T>): void {
        if (!items) {
            throw new Error("Items cannot be null.");
        }
        const otherSet = this.createComparableSet(items);
        const current = this.toArray();
        for (let i = 0; i < current.length; i++) {
            const item = current[i];
            if (!otherSet.Contains(item)) {
                this.Remove(item);
            }
        }
    }

    public SymmetricExceptWith(items: Iterable<T>): void {
        if (!items) {
            throw new Error("Items cannot be null.");
        }
        const otherSet = this.createComparableSet(items);
        const values = otherSet.toArray();
        for (let i = 0; i < values.length; i++) {
            const item = values[i];
            if (!this.Remove(item)) {
                this.Add(item);
            }
        }
    }

    public IsSubsetOf(items: Iterable<T>): boolean {
        if (!items) {
            throw new Error("Items cannot be null.");
        }
        const otherSet = this.createComparableSet(items);
        if (this._count > otherSet._count) {
            return false;
        }
        const current = this.toArray();
        for (let i = 0; i < current.length; i++) {
            if (!otherSet.Contains(current[i])) {
                return false;
            }
        }
        return true;
    }

    public IsSupersetOf(items: Iterable<T>): boolean {
        if (!items) {
            throw new Error("Items cannot be null.");
        }
        const otherSet = this.createComparableSet(items);
        const values = otherSet.toArray();
        for (let i = 0; i < values.length; i++) {
            if (!this.Contains(values[i])) {
                return false;
            }
        }
        return true;
    }

    public SetEquals(items: Iterable<T>): boolean {
        if (!items) {
            throw new Error("Items cannot be null.");
        }
        const otherSet = this.createComparableSet(items);
        if (this._count !== otherSet._count) {
            return false;
        }
        return this.IsSubsetOf(otherSet);
    }

    public GetEnumerator(): Iterator<T> {
        return this[Symbol.iterator]();
    }

    [Symbol.iterator](): Iterator<T> {
        const entries = this.toArray();
        let index = 0;
        return {
            next(): IteratorResult<T> {
                if (index < entries.length) {
                    return { value: entries[index++], done: false };
                }
                return { value: undefined as any, done: true };
            }
        };
    }
}
