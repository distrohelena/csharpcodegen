export interface ICollection<T> {
    readonly count: number;
    add(item: T): void;
    clear(): void;
    contains(item: T): boolean;
    remove(item: T): boolean;
    toArray(): T[];
}
