// @ts-nocheck
export interface IDictionary<TKey, TValue> {
    // Properties
    get keys(): TKey[];
    get values(): TValue[];
    get count(): number;

    // Methods
    add(key: TKey, value: TValue): void;
    TryAdd(key: TKey, value: TValue): boolean;
    containsKey(key: TKey): boolean;
    remove(key: TKey): boolean;
    tryGetValue(key: TKey, outValue: { value?: TValue }): boolean;
}
