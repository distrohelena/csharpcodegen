/// <reference lib="es2021.weakref" />
import { ArgumentException } from '../../argument.exception';
import { ArgumentNullException } from '../../argument-null.exception';
import { KeyValuePair } from '../../collections/generic/key-value-pair';

/** Identity-keyed ephemeron table: values may reference their keys without retaining either. */
export class ConditionalWeakTable<TKey extends object, TValue> implements Iterable<KeyValuePair<TKey, TValue>> {
    /** WeakMap provides the ephemeron lifetime semantics required by transaction buffers. */
    private Values = new WeakMap<TKey, TValue>();
    /** Only weak references are retained for diagnostic enumeration and LINQ Count. */
    private readonly References = new Set<WeakRef<TKey>>();
    /** Associates a live key with its enumeration handle without keeping the key alive. */
    private Handles = new WeakMap<TKey, WeakRef<TKey>>();
    /** Removes dead enumeration handles without capturing keys or values in a closure. */
    private readonly Cleanup = new FinalizationRegistry<WeakRef<TKey>>(reference => this.References.delete(reference));

    /** Adds a mapping, rejecting duplicates even when the original stored value is null. */
    Add(key: TKey, value: TValue): void {
        ConditionalWeakTable.ValidateKey(key);
        if (this.Values.has(key)) throw new ArgumentException('An item with the same key has already been added.', 'key');
        const reference = new WeakRef(key);
        this.Values.set(key, value);
        this.Handles.set(key, reference);
        this.References.add(reference);
        this.Cleanup.register(key, reference, reference);
    }

    /** Obtains the existing value or publishes one factory result; a reentrant insertion wins. */
    GetValue(key: TKey, createValueCallback: (key: TKey) => TValue): TValue {
        ConditionalWeakTable.ValidateKey(key);
        if (createValueCallback == null) throw new ArgumentNullException('createValueCallback');
        if (typeof createValueCallback !== 'function') throw new ArgumentException('A value factory must be callable.', 'createValueCallback');
        if (this.Values.has(key)) return this.Values.get(key)!;
        const created = createValueCallback(key);
        if (this.Values.has(key)) return this.Values.get(key)!;
        this.Add(key, created);
        return created;
    }

    /** Distinguishes a missing key from a present null value and clears the caller's out slot on failure. */
    TryGetValue(key: TKey, value: { value: TValue | null | undefined }): boolean {
        ConditionalWeakTable.ValidateKey(key);
        if (this.Values.has(key)) {
            value.value = this.Values.get(key)!;
            return true;
        }
        value.value = null;
        return false;
    }

    /** Removes one mapping and its weak enumeration handle, returning whether it existed. */
    Remove(key: TKey): boolean {
        ConditionalWeakTable.ValidateKey(key);
        if (!this.Values.delete(key)) return false;
        const reference = this.Handles.get(key)!;
        this.Cleanup.unregister(reference);
        this.References.delete(reference);
        this.Handles.delete(key);
        return true;
    }

    /** Releases every mapping while leaving no pending finalization registrations owned by this table. */
    Clear(): void {
        for (const reference of this.References) this.Cleanup.unregister(reference);
        this.References.clear();
        this.Values = new WeakMap<TKey, TValue>();
        this.Handles = new WeakMap<TKey, WeakRef<TKey>>();
    }

    /** Counts currently reachable entries without retaining keys between calls. */
    Count(): number {
        let count = 0;
        for (const unused of this) count += 1;
        return count;
    }

    /** Enumerates live pairs only; dead weak handles are pruned even before a finalizer runs. */
    *[Symbol.iterator](): IterableIterator<KeyValuePair<TKey, TValue>> {
        for (const reference of this.References) {
            const key = reference.deref();
            if (key === undefined) {
                this.References.delete(reference);
            } else if (this.Values.has(key)) {
                yield new KeyValuePair(key, this.Values.get(key)!);
            }
        }
    }

    /** Matches reference-type CLR key requirements instead of accepting JavaScript primitive keys. */
    private static ValidateKey(key: unknown): void {
        if (key == null) throw new ArgumentNullException('key');
        if (typeof key !== 'object' && typeof key !== 'function') throw new ArgumentException('A weak table key must be an object.', 'key');
    }
}
