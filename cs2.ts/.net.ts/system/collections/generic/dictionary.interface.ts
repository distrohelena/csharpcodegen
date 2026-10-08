// @ts-nocheck
import { IEnumerable } from "./ienumerable";
import { KeyValuePair } from "./key-value-pair";

/** Generic maps enumerate key/value pairs as required by their managed contract. */
export interface IDictionary<TKey, TValue> extends IEnumerable<KeyValuePair<TKey, TValue>> {
    // Properties
    get keys(): TKey[];
    get values(): TValue[];
    get count(): number;
    get Keys(): TKey[];
    get Values(): TValue[];
    get Count(): number;

    // Methods
    add(key: TKey, value: TValue): void;
    /** Converter representation of the C# dictionary indexer setter. */
    set(key: TKey, value: TValue): void;
    /** Converter representation of the C# dictionary indexer getter. */
    get(key: TKey): TValue | undefined;
    TryAdd(key: TKey, value: TValue): boolean;
    containsKey(key: TKey): boolean;
    remove(key: TKey): boolean;
    Remove(key: TKey): boolean;
    tryGetValue(key: TKey, outValue: { value?: TValue }): boolean;
    /** Preserves existing values and uses the supplied C# default only when the key is absent. */
    GetValueOrDefault(key: TKey, defaultValue: TValue): TValue;
}
