import { ArgumentOutOfRangeException } from '../argument-out-of-range.exception';
import { ObjectDisposedException } from '../object-disposed.exception';

type Waiter = {
    resolve: (signaled: boolean) => void;
    reject: (reason: unknown) => void;
    timer?: ReturnType<typeof setTimeout>;
};

/** Browser implementation of a manually signaled event; waits yield rather than blocking the event loop. */
export class ManualResetEventSlim {
    private readonly waiters = new Map<number, Waiter>();
    private nextWaiterId = 0;
    private disposed = false;
    private isSet: boolean;

    constructor(initialState: boolean = false) {
        this.isSet = initialState;
    }

    public get IsSet(): boolean {
        this.throwIfDisposed();
        return this.isSet;
    }

    /** Signals all current and future waiters until Reset is called. */
    public Set(): void {
        this.throwIfDisposed();
        if (this.isSet) return;
        this.isSet = true;
        const waiters = Array.from(this.waiters.values());
        this.waiters.clear();
        for (const waiter of waiters) {
            if (waiter.timer !== undefined) clearTimeout(waiter.timer);
            waiter.resolve(true);
        }
    }

    /** Clears the signal without cancelling waits that are already pending. */
    public Reset(): void {
        this.throwIfDisposed();
        this.isSet = false;
    }

    /** Resolves true when signaled, false at the timeout, or rejects when disposed while waiting. */
    public Wait(millisecondsTimeout: number = -1): Promise<boolean> {
        this.throwIfDisposed();
        if (!Number.isInteger(millisecondsTimeout) || millisecondsTimeout < -1) {
            throw new ArgumentOutOfRangeException('millisecondsTimeout');
        }
        if (this.isSet) return Promise.resolve(true);
        if (millisecondsTimeout === 0) return Promise.resolve(false);
        return new Promise<boolean>((resolve, reject) => {
            const waiterId = this.nextWaiterId++;
            const waiter: Waiter = { resolve, reject };
            if (millisecondsTimeout !== -1) {
                waiter.timer = setTimeout(() => {
                    if (this.waiters.delete(waiterId)) resolve(false);
                }, millisecondsTimeout);
            }
            this.waiters.set(waiterId, waiter);
        });
    }

    /** Releases every timer and rejects outstanding waits, matching access-after-disposal behavior. */
    public Dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        const waiters = Array.from(this.waiters.values());
        this.waiters.clear();
        for (const waiter of waiters) {
            if (waiter.timer !== undefined) clearTimeout(waiter.timer);
            waiter.reject(new ObjectDisposedException('ManualResetEventSlim'));
        }
    }

    private throwIfDisposed(): void {
        if (this.disposed) throw new ObjectDisposedException('ManualResetEventSlim');
    }
}