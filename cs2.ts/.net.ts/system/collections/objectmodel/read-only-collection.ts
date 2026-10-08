// @ts-nocheck
import { ArgumentNullException } from "../../argument-null.exception";
import { NotSupportedException } from "../../not-supported.exception";
import { IReadOnlyList } from "../generic/ireadonlylist";

export class ReadOnlyCollection<T> extends Array<T> implements IReadOnlyList<T> {
    constructor(list: Iterable<T>) {
        if (list == null) {
            throw new ArgumentNullException("list");
        }
        super();

        // A .NET ReadOnlyCollection is a live wrapper around its IList.  Keeping
        // the supplied Array/List as the proxy target preserves all array read
        // operations while the traps reject mutations through this view.
        const target = Array.isArray(list) ? list : Array.from(list);
        const readOnlyPrototype = new.target.prototype;
        return new Proxy(target, {
            get: (array, property, receiver) => {
                const ownMember = Reflect.getOwnPropertyDescriptor(readOnlyPrototype, property);
                if (ownMember !== undefined) {
                    return Reflect.get(readOnlyPrototype, property, receiver);
                }
                return Reflect.get(array, property, receiver);
            },
            getPrototypeOf: () => readOnlyPrototype,
            set: () => {
                throw new NotSupportedException("Collection is read-only.");
            },
            defineProperty: () => {
                throw new NotSupportedException("Collection is read-only.");
            },
            deleteProperty: () => {
                throw new NotSupportedException("Collection is read-only.");
            },
            setPrototypeOf: () => {
                throw new NotSupportedException("Collection is read-only.");
            },
            preventExtensions: () => {
                throw new NotSupportedException("Collection is read-only.");
            }
        }) as ReadOnlyCollection<T>;
    }

    public static get [Symbol.species](): ArrayConstructor {
        return Array;
    }

    public get count(): number {
        return this.length;
    }

    public get(index: number): T {
        if (index < 0 || index >= this.length) {
            throw new RangeError("Index out of range.");
        }
        return this[index];
    }

    public add(_item: T): void {
        throw new NotSupportedException("Collection is read-only.");
    }

    public addRange(_items: T[]): void {
        throw new NotSupportedException("Collection is read-only.");
    }

    public remove(_item: T): boolean {
        throw new NotSupportedException("Collection is read-only.");
    }

    public removeAt(_index: number): void {
        throw new NotSupportedException("Collection is read-only.");
    }

    public clear(): void {
        throw new NotSupportedException("Collection is read-only.");
    }

    public insert(_index: number, _item: T): void {
        throw new NotSupportedException("Collection is read-only.");
    }

    public removeRange(_index: number, _count: number): void {
        throw new NotSupportedException("Collection is read-only.");
    }

    public Sort(_comparer?: ((a: T, b: T) => number) | { Compare?: (a: T, b: T) => number }): void {
        throw new NotSupportedException("Collection is read-only.");
    }

    public sort(_compareFn?: (a: T, b: T) => number): this {
        throw new NotSupportedException("Collection is read-only.");
    }

    public push(..._items: T[]): number {
        throw new NotSupportedException("Collection is read-only.");
    }

    public pop(): T | undefined {
        throw new NotSupportedException("Collection is read-only.");
    }

    public splice(_start: number, _deleteCount?: number): T[] {
        throw new NotSupportedException("Collection is read-only.");
    }

    public shift(): T | undefined {
        throw new NotSupportedException("Collection is read-only.");
    }

    public unshift(..._items: T[]): number {
        throw new NotSupportedException("Collection is read-only.");
    }

    public reverse(): T[] {
        throw new NotSupportedException("Collection is read-only.");
    }

    public copyWithin(_target: number, _start: number, _end?: number): this {
        throw new NotSupportedException("Collection is read-only.");
    }

    public fill(_value: T, _start?: number, _end?: number): this {
        throw new NotSupportedException("Collection is read-only.");
    }
}
