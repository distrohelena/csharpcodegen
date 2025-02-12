export interface IDictionary<TKey, TValue> {
    // Properties
    get Keys(): TKey[];
    get Values(): TValue[];
    get Count(): number;

    // Methods
    Add(key: TKey, value: TValue): void;
    ContainsKey(key: TKey): boolean;
    Remove(key: TKey): boolean;
    TryGetValue(key: TKey, outValue: { value?: TValue }): boolean;
}
