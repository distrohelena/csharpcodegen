import { IDictionary } from "./dictionary.interface"
import { KeyValuePair } from "./key-value-pair";
import { List } from "./list";

export class Dictionary<TKey, TValue> implements IDictionary<TKey, TValue> {
    private items: { [key: string]: TValue } = {};
    private keyToString: (key: TKey) => string;
    private _count: number = 0;

    constructor(keyToString?: (key: TKey) => string) {
        this.keyToString = keyToString || ((key: TKey) => JSON.stringify(key));
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
        this.add(key, value);
    }

    // Get the value by key
    public get(key: TKey): TValue | undefined {
        const stringKey = this.keyToString(key);
        return this.items.hasOwnProperty(stringKey) ? this.items[stringKey] : undefined;
    }

    // Remove an item by key
    public remove(key: TKey): boolean {
        const stringKey = this.keyToString(key);
        if (this.items.hasOwnProperty(stringKey)) {
            delete this.items[stringKey];
            this._count--;
            return true;
        }
        return false;
    }

    // Check if a key exists
    public containsKey(key: TKey): boolean {
        const stringKey = this.keyToString(key);
        return this.items.hasOwnProperty(stringKey);
    }

    // Get the count of elements in the dictionary
    public get count(): number {
        return this.count;
    }

    // Get all keys in the dictionary
    public get keys(): TKey[] {
        return Object.keys(this.items).map(key => JSON.parse(key));
    }

    // Get all values in the dictionary
    public get values(): TValue[] {
        return Object.values(this.items);
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
            result.push({ key, value });
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
