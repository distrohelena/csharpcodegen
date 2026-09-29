import { ArgumentNullException } from '../../argument-null.exception';
import { ArgumentOutOfRangeException } from '../../argument-out-of-range.exception';
import { InvalidOperationException } from '../../invalid-operation.exception';

/** FIFO collection used by generated network send and download queues. */
export class Queue<T> implements Iterable<T> {
    /** Ring storage; dequeued slots are cleared so consumed packets can be collected. */
    private Items: Array<T | undefined> = [];
    /** Physical index of the next item to dequeue. */
    private Head = 0;
    /** Number of live entries in the ring. */
    private Size = 0;
    /** Mutation counter captured by each enumerator. */
    private Version = 0;

    /** Creates an empty queue without reserving storage. */
    constructor();
    /** Reserves capacity for an initially empty queue. */
    constructor(capacity: number);
    /** Copies items from an iterable in enumeration order. */
    constructor(collection: Iterable<T>);
    /** Selects the capacity or iterable constructor without treating explicit null as empty. */
    constructor(source?: number | Iterable<T>) {
        if (arguments.length === 0) return;
        if (typeof source === 'number') {
            if (!Number.isInteger(source) || source < 0 || source > 2147483647) {
                throw new ArgumentOutOfRangeException('capacity');
            }
            this.Items = new Array<T | undefined>(source);
        } else if (source === null || source === undefined) {
            throw new ArgumentNullException('collection');
        } else {
            for (const item of source) this.Enqueue(item);
        }
    }

    /** Returns the number of items available for dequeue. */
    public get Count(): number {
        return this.Size;
    }

    /** Appends an item at the tail, growing ring storage only when it is full. */
    public Enqueue(item: T): void {
        if (this.Size === this.Items.length) {
            const capacity = Math.max(4, this.Items.length * 2);
            const expanded = new Array<T | undefined>(capacity);
            for (let index = 0; index < this.Size; index++) expanded[index] = this.Items[(this.Head + index) % this.Items.length];
            this.Items = expanded;
            this.Head = 0;
        }
        this.Items[(this.Head + this.Size) % this.Items.length] = item;
        this.Size++;
        this.Version++;
    }

    /** Removes the oldest item, throwing the native empty-queue exception when no item exists. */
    public Dequeue(): T {
        const item = this.Peek();
        this.Items[this.Head] = undefined;
        this.Head = (this.Head + 1) % this.Items.length;
        this.Size--;
        this.Version++;
        return item;
    }

    /** Reads the oldest item without removing it or invalidating enumerators. */
    public Peek(): T {
        if (this.Size === 0) throw new InvalidOperationException('Queue empty.');
        return this.Items[this.Head] as T;
    }

    /** Removes all entries, releases their references and retains allocated capacity. */
    public Clear(): void {
        this.Items.fill(undefined);
        this.Head = 0;
        this.Size = 0;
        this.Version++;
    }

    /** Tests primitive value or object identity, including equality of NaN values. */
    public Contains(item: T): boolean {
        for (let index = 0; index < this.Size; index++) {
            const candidate = this.Items[(this.Head + index) % this.Items.length];
            if (candidate === item || (Number.isNaN(candidate) && Number.isNaN(item))) return true;
        }
        return false;
    }

    /** Copies the live entries in FIFO order without exposing the ring storage. */
    public ToArray(): T[] {
        const result = new Array<T>(this.Size);
        for (let index = 0; index < this.Size; index++) result[index] = this.Items[(this.Head + index) % this.Items.length] as T;
        return result;
    }

    /** Captures the current mutation version before the first iterator step. */
    public [Symbol.iterator](): IterableIterator<T> {
        return this.Enumerate(this.Version);
    }

    /** Enumerates FIFO entries and rejects mutations even before the first or final step. */
    private *Enumerate(expectedVersion: number): IterableIterator<T> {
        for (let index = 0; index < this.Size; index++) {
            if (this.Version !== expectedVersion) throw new InvalidOperationException('Collection was modified; enumeration operation may not execute.');
            yield this.Items[(this.Head + index) % this.Items.length] as T;
        }
        if (this.Version !== expectedVersion) throw new InvalidOperationException('Collection was modified; enumeration operation may not execute.');
    }
}
