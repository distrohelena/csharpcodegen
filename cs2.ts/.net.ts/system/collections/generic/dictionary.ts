import { IDictionary } from "./dictionary.interface"

export class Dictionary<TKey, TValue> implements IDictionary<TKey, TValue> {
    private items: { [key: string]: TValue } = {};
    private keyToString: (key: TKey) => string;
    private count: number = 0;

    constructor(keyToString?: (key: TKey) => string) {
        this.keyToString = keyToString || ((key: TKey) => JSON.stringify(key));
    }

    // Add a key-value pair to the dictionary
    public Add(key: TKey, value: TValue): void {
        const stringKey = this.keyToString(key);
        if (this.items.hasOwnProperty(stringKey)) {
            throw new Error("Key already exists in dictionary.");
        }
        this.items[stringKey] = value;
        this.count++;
    }

    // Try to get the value by key, returning a boolean for success/failure
    public TryGetValue(key: TKey, outValue: { value?: TValue }): boolean {
        const stringKey = this.keyToString(key);
        if (this.items.hasOwnProperty(stringKey)) {
            outValue.value = this.items[stringKey];
            return true;
        }
        outValue.value = undefined;
        return false;
    }

    // Get the value by key using indexing
    public get(key: TKey): TValue | undefined {
        return this.Get(key);
    }

    // Set value by key using indexing
    public set(key: TKey, value: TValue): void {
        this.Add(key, value);
    }

    // Get the value by key
    public Get(key: TKey): TValue | undefined {
        const stringKey = this.keyToString(key);
        return this.items.hasOwnProperty(stringKey) ? this.items[stringKey] : undefined;
    }

    // Remove an item by key
    public Remove(key: TKey): boolean {
        const stringKey = this.keyToString(key);
        if (this.items.hasOwnProperty(stringKey)) {
            delete this.items[stringKey];
            this.count--;
            return true;
        }
        return false;
    }

    // Check if a key exists
    public ContainsKey(key: TKey): boolean {
        const stringKey = this.keyToString(key);
        return this.items.hasOwnProperty(stringKey);
    }

    // Get the count of elements in the dictionary
    public get Count(): number {
        return this.count;
    }

    // Get all keys in the dictionary
    public get Keys(): TKey[] {
        return Object.keys(this.items).map(key => JSON.parse(key));
    }

    // Get all values in the dictionary
    public get Values(): TValue[] {
        return Object.values(this.items);
    }

    // Clear the dictionary
    public Clear(): void {
        this.items = {};
        this.count = 0;
    }

    // Iterate over the dictionary using a callback function
    public ForEach(callback: (key: TKey, value: TValue) => void): void {
        for (const key in this.items) {
            if (this.items.hasOwnProperty(key)) {
                callback(JSON.parse(key), this.items[key]);
            }
        }
    }
}
