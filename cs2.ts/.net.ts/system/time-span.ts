// @ts-nocheck
export class TimeSpan {
    private _milliseconds: number;

    /// <summary>
    /// Represents the number of nanoseconds per tick. This field is constant.
    /// </summary>
    /// <remarks>
    /// The value of this constant is 100.
    /// </remarks>
    public static readonly NanosecondsPerTick = 100;

    /// <summary>
    /// Represents the number of ticks in 1 microsecond. This field is constant.
    /// </summary>
    /// <remarks>
    /// The value of this constant is 10.
    /// </remarks>
    public static readonly TicksPerMicrosecond = 10;

    /// <summary>
    /// Represents the number of ticks in 1 millisecond. This field is constant.
    /// </summary>
    /// <remarks>
    /// The value of this constant is 10 thousand; that is, 10,000.
    /// </remarks>
    public static readonly TicksPerMillisecond = this.TicksPerMicrosecond * 1000;
    public static readonly TicksPerSecond = this.TicksPerMillisecond * 1000;   // 10,000,000
    public static readonly TicksPerMinute = this.TicksPerSecond * 60;         // 600,000,000
    public static readonly TicksPerHour = this.TicksPerMinute * 60;        // 36,000,000,000
    public static readonly TicksPerDay = this.TicksPerHour * 24;  

    // Constructor that takes ticks (1 tick = 1/10,000 of a millisecond)
    constructor(ticks: number);
    // Constructor for specifying days, hours, minutes, seconds, milliseconds
    constructor(days: number, hours: number, minutes: number, seconds: number, milliseconds: number);
    constructor(arg1: number, hours: number = 0, minutes: number = 0, seconds: number = 0, milliseconds: number = 0) {
        if (arguments.length === 1) {
            // Assume ticks were passed
            this._milliseconds = arg1 / 10000;
        } else {
            // Use days, hours, minutes, seconds, milliseconds
            this._milliseconds = (((arg1 * 24 + hours) * 60 + minutes) * 60 + seconds) * 1000 + milliseconds;
        }
    }

    // Factory methods
    public static fromTicks(ticks: number): TimeSpan {
        return new TimeSpan(ticks);
    }

    public static fromDays(days: number): TimeSpan {
        return new TimeSpan(days, 0, 0, 0, 0);
    }

    public static fromHours(hours: number): TimeSpan {
        return new TimeSpan(0, hours, 0, 0, 0);
    }

    public static fromMinutes(minutes: number): TimeSpan {
        return new TimeSpan(0, 0, minutes, 0, 0);
    }

    public static fromSeconds(seconds: number): TimeSpan {
        return new TimeSpan(0, 0, 0, seconds, 0);
    }

    public static fromMilliseconds(milliseconds: number): TimeSpan {
        return new TimeSpan(0, 0, 0, 0, milliseconds);
    }

    // Properties
    public get Days(): number {
        return Math.floor(this._milliseconds / (24 * 60 * 60 * 1000));
    }

    public get Hours(): number {
        return Math.floor(this._milliseconds / (60 * 60 * 1000)) % 24;
    }

    public get Minutes(): number {
        return Math.floor(this._milliseconds / (60 * 1000)) % 60;
    }

    public get Seconds(): number {
        return Math.floor(this._milliseconds / 1000) % 60;
    }

    public get Milliseconds(): number {
        return this._milliseconds % 1000;
    }

    public get Ticks(): number {
        return this._milliseconds * 10000; // Convert milliseconds back to ticks
    }

    public get TotalDays(): number {
        return this._milliseconds / (24 * 60 * 60 * 1000);
    }

    public get TotalHours(): number {
        return this._milliseconds / (60 * 60 * 1000);
    }

    public get TotalMinutes(): number {
        return this._milliseconds / (60 * 1000);
    }

    public get TotalSeconds(): number {
        return this._milliseconds / 1000;
    }

    public get TotalMilliseconds(): number {
        return this._milliseconds;
    }

    // Methods
    public add(ts: TimeSpan): TimeSpan {
        return new TimeSpan(0, 0, 0, 0, this._milliseconds + ts.TotalMilliseconds);
    }

    public subtract(ts: TimeSpan): TimeSpan {
        return new TimeSpan(0, 0, 0, 0, this._milliseconds - ts.TotalMilliseconds);
    }

    public negate(): TimeSpan {
        return new TimeSpan(0, 0, 0, 0, -this._milliseconds);
    }

    public toString(): string {
        const days = this.Days > 0 ? `${this.Days}.` : '';
        const hours = this.Hours.toString().padStart(2, '0');
        const minutes = this.Minutes.toString().padStart(2, '0');
        const seconds = this.Seconds.toString().padStart(2, '0');
        const milliseconds = this.Milliseconds > 0 ? `.${this.Milliseconds.toString().padStart(3, '0')}` : '';
        return `${days}${hours}:${minutes}:${seconds}${milliseconds}`;
    }

    // Static methods for comparison and equality
    public static compare(ts1: TimeSpan, ts2: TimeSpan): number {
        return ts1.TotalMilliseconds - ts2.TotalMilliseconds;
    }

    public static equals(ts1: TimeSpan, ts2: TimeSpan): boolean {
        return ts1.TotalMilliseconds === ts2.TotalMilliseconds;
    }

    public duration(): TimeSpan {
        return new TimeSpan(0, 0, 0, 0, Math.abs(this._milliseconds));
    }
}
