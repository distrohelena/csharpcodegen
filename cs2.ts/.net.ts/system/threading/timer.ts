import { ArgumentException } from '../argument.exception';
import { ArgumentNullException } from '../argument-null.exception';
import { ArgumentOutOfRangeException } from '../argument-out-of-range.exception';
import { TimeSpan } from '../time-span';

/** Monotonic browser implementation of the managed Timer scheduling and cancellation contract. */
export class Timer {
    /** Largest delay accepted by the CLR timer APIs, excluding the infinite sentinel. */
    private static readonly MaximumTimeout = 4294967294;
    /** Largest host delay that does not overflow signed browser/Node timeout storage. */
    private static readonly MaximumHostDelay = 2147483647;
    /** Callback invoked with the original state after the due time elapses. */
    private readonly Callback: (state: unknown) => void;
    /** Original callback state, or this timer for the callback-only constructor. */
    private readonly State: unknown;
    /** Handle of the currently scheduled host timeout, if any. */
    private Handle: ReturnType<typeof setTimeout> | undefined;
    /** Monotonic deadline of the next callback, including long-delay segments. */
    private DueAt = 0;
    /** Current repeat interval; zero and minus one both select one-shot behavior. */
    private Period = -1;
    /** Version used to reject callbacks that were queued before Change or Dispose. */
    private Generation = 0;
    /** True after disposal; future valid Change calls return false like modern .NET. */
    private Disposed = false;

    /** Creates a disabled timer by default, or schedules the supplied numeric/TimeSpan deadlines. */
    constructor(callback: (state: unknown) => void, state?: unknown, dueTime: number | TimeSpan = -1, period: number | TimeSpan = -1) {
        if (callback == null) throw new ArgumentNullException('callback');
        if (typeof callback !== 'function') throw new ArgumentException('A timer callback must be callable.', 'callback');
        this.Callback = callback;
        this.State = arguments.length === 1 ? this : state;
        this.Change(dueTime, period);
    }

    /** Replaces pending scheduling; a due time of minus one disables the timer without disposing it. */
    Change(dueTime: number | TimeSpan, period: number | TimeSpan): boolean {
        const delay = Timer.ReadTimeout(dueTime, 'dueTime');
        const interval = Timer.ReadTimeout(period, 'period');
        if (this.Disposed) return false;
        this.CancelPending();
        this.Period = interval;
        if (delay !== -1) {
            this.DueAt = performance.now() + delay;
            this.Schedule(this.Generation);
        }
        return true;
    }

    /** Cancels future callbacks and releases the active host timeout; repeated disposal is harmless. */
    Dispose(): void {
        if (this.Disposed) return;
        this.Disposed = true;
        this.CancelPending();
    }

    /** Supports the lower-case disposable convention used by generated interface calls. */
    dispose(): void { this.Dispose(); }

    /** Validates CLR timeout bounds and applies TimeSpan's truncation to whole milliseconds. */
    private static ReadTimeout(value: number | TimeSpan, name: string): number {
        const milliseconds = value instanceof TimeSpan ? Math.trunc(value.TotalMilliseconds) : value;
        if (typeof milliseconds !== 'number' || !Number.isSafeInteger(milliseconds)
            || milliseconds < -1 || milliseconds > Timer.MaximumTimeout) {
            throw new ArgumentOutOfRangeException(name, 'Timeout must be -1 or between 0 and 4294967294 milliseconds.');
        }
        return milliseconds;
    }

    /** Invalidates callbacks before releasing their handles, including callbacks already queued by the host. */
    private CancelPending(): void {
        this.Generation += 1;
        if (this.Handle !== undefined) clearTimeout(this.Handle);
        this.Handle = undefined;
    }

    /** Splits long CLR deadlines into safe host timeout segments using a monotonic clock. */
    private Schedule(generation: number): void {
        const delay = Math.min(Timer.MaximumHostDelay, Math.max(0, Math.ceil(this.DueAt - performance.now())));
        this.Handle = setTimeout(() => this.OnTimeout(generation), delay);
    }

    /** Completes a long-delay segment or publishes one callback, allowing it to rearm or dispose safely. */
    private OnTimeout(generation: number): void {
        if (this.Disposed || generation !== this.Generation) return;
        this.Handle = undefined;
        if (performance.now() < this.DueAt) {
            this.Schedule(generation);
            return;
        }
        if (this.Period > 0) {
            this.DueAt = performance.now() + this.Period;
            this.Schedule(generation);
        }
        this.Callback(this.State);
    }
}