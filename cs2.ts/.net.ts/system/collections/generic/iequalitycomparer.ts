// @ts-nocheck
export interface IEqualityComparer<TKey> {
    Equals(x: TKey, y: TKey): boolean;
    GetHashCode(obj: TKey): number;
}
