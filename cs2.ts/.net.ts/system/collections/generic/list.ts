// @ts-nocheck
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
    public addRange(items: T[]): void {
        this.push(...items);
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
    public contains(item: T): boolean {
        return this.indexOf(item) !== -1;
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

    // Get all items in the list as an array
    public toArray(): T[] {
        return [...this];
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
        toList(): List<T>;
        Any(predicate?: (item: T) => boolean): boolean;
        Where(predicate: (item: T, index: number) => boolean): T[];
        ToArray(): T[];
    }
}

if (!(Array.prototype as any).toList) {
    (Array.prototype as any).toList = function () {
        return new List(...this);
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

if (!(Array.prototype as any).Where) {
    (Array.prototype as any).Where = function (predicate: (item: any, index: number) => boolean) {
        if (!predicate) {
            throw new Error("Predicate cannot be null.");
        }
        return this.filter((item: any, index: number) => predicate(item, index));
    };
}

if (!(Array.prototype as any).ToArray) {
    (Array.prototype as any).ToArray = function () {
        return [...this];
    };
}
