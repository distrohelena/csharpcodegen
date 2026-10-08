// @ts-nocheck
import { ReadOnlyCollection } from "../objectmodel/read-only-collection";
import { IEqualityComparer } from "./iequalitycomparer";

type Ordering<T> = {
    selector: (item: T) => any;
    comparer?: { Compare?: (left: any, right: any) => number };
    descending: boolean;
};

const orderingState = Symbol("cs2.ts.ordering-state");

function valuesEqual<T>(left: T, right: T, comparer?: IEqualityComparer<T>): boolean {
    return comparer ? comparer.Equals(left, right) : left === right;
}

function compareValues(left: any, right: any, comparer?: { Compare?: (left: any, right: any) => number }): number {
    if (comparer?.Compare) {
        return comparer.Compare(left, right);
    }
    if (left === right) return 0;
    if (left == null) return -1;
    if (right == null) return 1;
    return left < right ? -1 : left > right ? 1 : 0;
}

function orderBy<T>(items: T[], orderings: Ordering<T>[]): T[] {
    const ordered = [...items].sort((left, right) => {
        for (const ordering of orderings) {
            const comparison = compareValues(ordering.selector(left), ordering.selector(right), ordering.comparer);
            if (comparison !== 0) {
                return ordering.descending ? -comparison : comparison;
            }
        }
        return 0;
    });
    Object.defineProperty(ordered, orderingState, { value: orderings, configurable: true });
    return ordered;
}

export class List<T> extends Array<T> {
    // Initialize a new list, optionally with items or a capacity placeholder
    constructor();
    constructor(capacity: number);
    constructor(items: Iterable<T>);
    constructor(...items: T[]);
    constructor(arg1?: number | Iterable<T> | T, ...rest: T[]) {
        super();

        if (typeof arg1 === "number" && rest.length === 0) {
            // capacity is ignored in JS implementation
            return;
        }

        if (rest.length > 0) {
            this.push(arg1 as T, ...rest);
            return;
        }

        if (arg1 && typeof (arg1 as any)[Symbol.iterator] === "function") {
            for (const item of arg1 as Iterable<T>) {
                this.push(item);
            }
        } else if (arg1 !== undefined) {
            this.push(arg1 as T);
        }
    }

    // Add an item to the list
    public add(item: T): void {
        this.push(item);
    }

    // Add multiple items to the list
    public addRange(items: Iterable<T> | T[]): void {
        if (items == null) {
            throw new Error("Items cannot be null.");
        }
        if (Array.isArray(items)) {
            this.push(...items);
            return;
        }
        for (const item of items) {
            this.push(item);
        }
    }

    // Remove an item from the list (first occurrence)
    public remove(item: T): boolean {
        const index = this.indexOf(item);
        if (index !== -1) {
            this.splice(index, 1);
            return true;
        }
        return false;
    }

    // Remove an item at a specific index
    public removeAt(index: number): void {
        if (index >= 0 && index < this.length) {
            this.splice(index, 1);
        } else {
            throw new Error("Index out of range.");
        }
    }

    // Clear the list
    public clear(): void {
        this.length = 0;
        //this.items = [];
    }

    // Check if the list contains an item
    public contains(item: T, comparer?: IEqualityComparer<T>): boolean {
        return this.some(value => valuesEqual(value, item, comparer));
    }

    // Get the item at a specific index
    public get(index: number): T {
        if (index >= 0 && index < this.length) {
            return this[index];
        } else {
            throw new Error("Index out of range.");
        }
    }

    // Find all items that match a predicate
    public findAll(predicate: (item: T) => boolean): T[] {
        return this.filter(predicate);
    }

    // Get the number of items in the list
    public get count(): number {
        return this.length;
    }

    // Get the number of items in the list (C#-style)
    public get Count(): number {
        return this.length;
    }

    // Get all items in the list as an array
    public toArray(): T[] {
        return [...this];
    }

    // Expose the same backing list through a mutation-blocking view, matching
    // System.Collections.Generic.List<T>.AsReadOnly rather than taking a snapshot.
    public AsReadOnly(): ReadOnlyCollection<T> {
        return new ReadOnlyCollection<T>(this);
    }

    // Sorts the list in place
    public Sort(comparer?: ((a: T, b: T) => number) | { Compare?: (a: T, b: T) => number }): void {
        if (typeof comparer === "function") {
            Array.prototype.sort.call(this, comparer);
        } else if (comparer && typeof (comparer as any).Compare === "function") {
            Array.prototype.sort.call(this, (a: T, b: T) => (comparer as any).Compare(a, b));
        } else {
            Array.prototype.sort.call(this);
        }
    }

    // Insert an item at a specific index
    public insert(index: number, item: T): void {
        if (index >= 0 && index <= this.length) {
            this.splice(index, 0, item);
        } else {
            throw new Error("Index out of range.");
        }
    }

    // Get a range of elements starting at index, with specified count
    public getRange(index: number, count: number): List<T> {
        if (index < 0 || count < 0 || index + count > this.length) {
            throw new Error("Index and count must be non-negative and within the bounds of the list.");
        }
        const range = this.slice(index, index + count);
        return new List<T>(...range);
    }

    /** Mirrors List<T>.Reverse by mutating this list in place. */
    public Reverse(): void {
        Array.prototype.reverse.call(this);
    }

    // Remove a range of elements starting at index, with specified count
    public removeRange(index: number, count: number): void {
        if (index < 0 || count < 0 || index + count > this.length) {
            throw new Error("Index and count must be non-negative and within the bounds of the list.");
        }
        this.splice(index, count);
    }

}

declare global {
    interface Array<T> {
        readonly count: number;
        readonly Count: number;
        toList(): List<T>;
        Any(predicate?: (item: T) => boolean): boolean;
        contains(item: T, comparer?: IEqualityComparer<T>): boolean;
        Contains(item: T, comparer?: IEqualityComparer<T>): boolean;
        Distinct(comparer?: IEqualityComparer<T>): T[];
        SequenceEqual(other: Iterable<T>, comparer?: IEqualityComparer<T>): boolean;
        FirstOrDefault(predicate?: (item: T, index: number) => boolean): T | undefined;
        Where(predicate: (item: T, index: number) => boolean): T[];
        Select<TResult>(selector: (item: T, index: number) => TResult): TResult[];
        ToArray(): T[];
        OrderBy<TKey>(selector: (item: T) => TKey, comparer?: { Compare?: (left: TKey, right: TKey) => number }): T[];
        OrderByDescending<TKey>(selector: (item: T) => TKey, comparer?: { Compare?: (left: TKey, right: TKey) => number }): T[];
        ThenBy<TKey>(selector: (item: T) => TKey, comparer?: { Compare?: (left: TKey, right: TKey) => number }): T[];
        ThenByDescending<TKey>(selector: (item: T) => TKey, comparer?: { Compare?: (left: TKey, right: TKey) => number }): T[];
    }
}

if (!(Array.prototype as any).toList) {
    (Array.prototype as any).toList = function () {
        // Pass the array itself so the iterable constructor branch copies it element-wise.
        // Spreading (`new List(...this)`) mis-binds single-element arrays: a lone string hits the
        // iterable branch and char-splits, a lone number hits the capacity branch and vanishes.
        return new List(this);
    };
}

if (!(Array.prototype as any).Any) {
    (Array.prototype as any).Any = function (predicate?: (item: any) => boolean) {
        if (predicate) {
            return this.some((item: any) => predicate(item));
        }
        return this.length > 0;
    };
}

if (!(Array.prototype as any).contains) {
    (Array.prototype as any).contains = function (item: any, comparer?: IEqualityComparer<any>) {
        return this.some((value: any) => valuesEqual(value, item, comparer));
    };
}

if (!(Array.prototype as any).Contains) {
    (Array.prototype as any).Contains = function (item: any, comparer?: IEqualityComparer<any>) {
        return this.contains(item, comparer);
    };
}

if (!(Array.prototype as any).Distinct) {
    (Array.prototype as any).Distinct = function (comparer?: IEqualityComparer<any>) {
        const distinct: any[] = [];
        for (const item of this) {
            if (!distinct.some(value => valuesEqual(value, item, comparer))) {
                distinct.push(item);
            }
        }
        return distinct;
    };
}

if (!(Array.prototype as any).SequenceEqual) {
    (Array.prototype as any).SequenceEqual = function (other: Iterable<any>, comparer?: IEqualityComparer<any>) {
        if (other == null) {
            return false;
        }
        const iterator = other[Symbol.iterator]();
        for (const item of this) {
            const next = iterator.next();
            if (next.done || !valuesEqual(item, next.value, comparer)) {
                return false;
            }
        }
        return iterator.next().done === true;
    };
}

if (!(Array.prototype as any).FirstOrDefault) {
    (Array.prototype as any).FirstOrDefault = function (predicate?: (item: any, index: number) => boolean) {
        if (predicate === undefined) {
            return this.length > 0 ? this[0] : undefined;
        }
        if (!predicate) {
            throw new Error("Predicate cannot be null.");
        }
        for (let i = 0; i < this.length; i++) {
            if (predicate(this[i], i)) {
                return this[i];
            }
        }
        return undefined;
    };
}

if (!(Array.prototype as any).Where) {
    (Array.prototype as any).Where = function (predicate: (item: any, index: number) => boolean) {
        if (!predicate) {
            throw new Error("Predicate cannot be null.");
        }
        return this.filter((item: any, index: number) => predicate(item, index));
    };
}

if (!(Array.prototype as any).Select) {
    (Array.prototype as any).Select = function (selector: (item: any, index: number) => any) {
        if (!selector) {
            throw new Error("Selector cannot be null.");
        }
        return this.map((item: any, index: number) => selector(item, index));
    };
}

if (!(Array.prototype as any).ToArray) {
    (Array.prototype as any).ToArray = function () {
        return [...this];
    };
}

if (!Object.getOwnPropertyDescriptor(Array.prototype, "count")) {
    Object.defineProperty(Array.prototype, "count", {
        get: function () {
            return this.length;
        },
        enumerable: false
    });
}

if (!Object.getOwnPropertyDescriptor(Array.prototype, "Count")) {
    Object.defineProperty(Array.prototype, "Count", {
        get: function () {
            return this.length;
        },
        enumerable: false
    });
}

if (!(Array.prototype as any).OrderBy) {
    (Array.prototype as any).OrderBy = function (selector: (item: any) => any, comparer?: { Compare?: (left: any, right: any) => number }) {
        if (!selector) {
            throw new Error("Selector cannot be null.");
        }
        return orderBy(this, [{ selector, comparer, descending: false }]);
    };
}

if (!(Array.prototype as any).OrderByDescending) {
    (Array.prototype as any).OrderByDescending = function (selector: (item: any) => any, comparer?: { Compare?: (left: any, right: any) => number }) {
        if (!selector) {
            throw new Error("Selector cannot be null.");
        }
        return orderBy(this, [{ selector, comparer, descending: true }]);
    };
}

if (!(Array.prototype as any).ThenBy) {
    (Array.prototype as any).ThenBy = function (selector: (item: any) => any, comparer?: { Compare?: (left: any, right: any) => number }) {
        if (!selector) {
            throw new Error("Selector cannot be null.");
        }
        const preceding = (this as any)[orderingState] ?? [];
        return orderBy(this, [...preceding, { selector, comparer, descending: false }]);
    };
}

if (!(Array.prototype as any).ThenByDescending) {
    (Array.prototype as any).ThenByDescending = function (selector: (item: any) => any, comparer?: { Compare?: (left: any, right: any) => number }) {
        if (!selector) {
            throw new Error("Selector cannot be null.");
        }
        const preceding = (this as any)[orderingState] ?? [];
        return orderBy(this, [...preceding, { selector, comparer, descending: true }]);
    };
}
