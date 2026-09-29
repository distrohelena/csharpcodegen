import { TimeSpan } from '../time-span';

/** Measures elapsed monotonic time, retaining the lower-case names used by native conversion. */
export class Stopwatch {
    /** Timestamp of the current running interval, in microsecond counter ticks. */
    private StartedAt = 0;
    /** True while the current interval contributes to elapsed time. */
    private Running = false;
    /** Accumulated counter ticks from completed running intervals. */
    private AccumulatedTicks = 0;

    /** Number of microsecond counter ticks per second for the performance.now-backed clock. */
    public static readonly Frequency = 1000000;

    /** Reports whether elapsed time is currently advancing. */
    public get isRunning(): boolean {
        return this.Running;
    }

    /** Returns total elapsed counter ticks without changing stopwatch state. */
    public get elapsedTicks(): number {
        return this.AccumulatedTicks + (this.Running ? Stopwatch.getTimestamp() - this.StartedAt : 0);
    }

    /** Returns completed milliseconds, truncating the fractional millisecond as .NET does. */
    public get ElapsedMilliseconds(): number {
        return Math.trunc(this.elapsedTicks / 1000);
    }

    /** Converts counter ticks to TimeSpan ticks while retaining fractional milliseconds. */
    public get elapsed(): TimeSpan {
        return new TimeSpan(this.elapsedTicks * 10);
    }

    /** Returns a monotonic timestamp in this shim's microsecond counter units. */
    public static getTimestamp(): number {
        return Math.trunc(performance.now() * 1000);
    }

    /** Creates and starts an initially empty stopwatch. */
    public static startNew(): Stopwatch {
        return new Stopwatch().start();
    }

    /** Starts or resumes measurement; starting an already running stopwatch has no effect. */
    public start(): Stopwatch {
        if (!this.Running) {
            this.StartedAt = Stopwatch.getTimestamp();
            this.Running = true;
        }
        return this;
    }

    /** Clears all elapsed time and starts a new running interval. */
    public restart(): void {
        this.AccumulatedTicks = 0;
        this.StartedAt = Stopwatch.getTimestamp();
        this.Running = true;
    }

    /** Stops the current interval, preserving prior intervals and ignoring repeated stops. */
    public stop(): void {
        if (!this.Running) return;
        this.AccumulatedTicks += Stopwatch.getTimestamp() - this.StartedAt;
        this.Running = false;
    }

    /** Stops measurement and clears all accumulated elapsed time. */
    public reset(): void {
        this.AccumulatedTicks = 0;
        this.StartedAt = 0;
        this.Running = false;
    }
}
