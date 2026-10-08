import { ArgumentOutOfRangeException } from '../argument-out-of-range.exception';
import { TimeSpan } from '../time-span';
/** Schedules CLR-sized delays without overflowing the browser's signed timer limit. */
export class CancellationTimer {
    /** Monotonic expiration timestamp. */
    private readonly deadline: number;
    /** Current browser timeout, if one is scheduled. */
    private timer: ReturnType<typeof setTimeout> | undefined;
    /** Callback retained until expiration or disposal. */
    private callback: (() => void) | undefined;
    /** Starts a finite timer, splitting long durations into safe browser-sized intervals. */
    constructor(milliseconds: number, callback: () => void) {
        this.deadline = performance.now() + milliseconds;
        this.callback = callback;
        this.Schedule();
    }
    /** Converts an integer or TimeSpan duration and validates the CLR timeout range. */
    public static Milliseconds(delay: number | TimeSpan): number {
        const duration = typeof delay === 'number' ? delay : Math.trunc(delay.TotalMilliseconds);
        const maximum = typeof delay === 'number' ? 2147483647 : 4294967294;
        if (!Number.isInteger(duration) || duration < -1 || duration > maximum) {
            throw new ArgumentOutOfRangeException('delay');
        }
        return duration;
    }
    /** Releases the pending timeout without invoking its callback. */
    public Dispose(): void {
        if (this.timer !== undefined) clearTimeout(this.timer);
        this.timer = undefined;
        this.callback = undefined;
    }
    /** Schedules the next timer interval using the monotonic remaining duration. */
    private Schedule(): void {
        this.timer = setTimeout(() => this.Elapsed(), Math.min(2147483647, Math.max(0, this.deadline - performance.now())));
    }
    /** Reschedules an early interval or invokes the callback once at expiration. */
    private Elapsed(): void {
        if (!this.callback) return;
        if (performance.now() < this.deadline) { this.Schedule(); return; }
        const callback = this.callback;
        this.Dispose();
        callback();
    }
}
