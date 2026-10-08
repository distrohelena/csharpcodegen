// @ts-nocheck
import { IEnumerable } from "./ienumerable";
import { KeyValuePair } from "./key-value-pair";

export interface IReadOnlyDictionary<TKey, TValue> extends IEnumerable<KeyValuePair<TKey, TValue>> {
    readonly keys?: TKey[];
    readonly values?: TValue[];
    readonly count?: number;
    readonly Keys?: TKey[];
    readonly Values?: TValue[];
    readonly Count?: number;
    containsKey(key: TKey): boolean;
    tryGetValue(key: TKey, outValue: { value?: TValue }): boolean;
    /** Preserves existing values and uses the supplied C# default only when the key is absent. */
    GetValueOrDefault(key: TKey, defaultValue: TValue): TValue;
    get(key: TKey): TValue | undefined;
}
