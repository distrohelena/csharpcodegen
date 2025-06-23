export class SortedList<TKey, TValue> {
    private items: Array<[TKey, TValue]> = [];
    private comparer: (a: TKey, b: TKey) => number;

    constructor(comparer: (a: TKey, b: TKey) => number) {
        this.comparer = comparer;
        return new Proxy(this, {
            get: (target, prop: string) => {
                return target.Get(prop as unknown as TKey);
            },
            set: (target, prop: string, value: TValue) => {
                target.add(prop as unknown as TKey, value);
                return true;
            }
        });
    }

    // Add a key-value pair to the list
    public add(key: TKey, value: TValue): void {
        this.items.push([key, value]);
        this.Sort();
    }

    // Remove an item by key
    public Remove(key: TKey): boolean {
        const index = this.FindIndex(key);
        if (index !== -1) {
            this.items.splice(index, 1);
            return true;
        }
        return false;
    }

    // Get a value by key
    public Get(key: TKey): TValue | undefined {
        const index = this.FindIndex(key);
        return index !== -1 ? this.items[index][1] : undefined;
    }

    // Check if the list contains the key
    public ContainsKey(key: TKey): boolean {
        return this.FindIndex(key) !== -1;
    }

    // Get the count of elements in the list
    public Count(): number {
        return this.items.length;
    }

    // Get all keys in sorted order
    public Keys(): TKey[] {
        return this.items.map(([key]) => key);
    }

    // Get all values in sorted order
    public Values(): TValue[] {
        return this.items.map(([, value]) => value);
    }

    public Clear() {
        this.items = [];
    }

    // Sort the list by the key using the comparer
    private Sort(): void {
        this.items.sort(([keyA], [keyB]) => this.comparer(keyA, keyB));
    }

    // Find the index of a key
    private FindIndex(key: TKey): number {
        return this.items.findIndex(([itemKey]) => this.comparer(itemKey, key) === 0);
    }

    public tryGetValue(key: TKey, outValue: { value?: TValue }): boolean {
        if (this.ContainsKey(key)) {
            outValue.value = this.Get(key);
            return true;
        }
        outValue.value = undefined;
        return false;
    }
}
