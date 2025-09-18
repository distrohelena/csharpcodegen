// @ts-nocheck
import { TimeSpan } from "./time-span";

export class DateTime {
    private _date: Date;

    // Constructor
    constructor(year: number, month: number, day: number, hours: number = 0, minutes: number = 0, seconds: number = 0, milliseconds: number = 0) {
        this._date = new Date(Date.UTC(year, month - 1, day, hours, minutes, seconds, milliseconds)); // UTC by default
    }

    // Static properties for current UTC and local time
    public static get UtcNow(): DateTime {
        return new DateTime(1970, 1, 1).AddMilliseconds(Date.now()); // Uses current UTC time
    }

    public static get Now(): DateTime {
        const now = new Date();
        return new DateTime(
            now.getFullYear(),
            now.getMonth() + 1,
            now.getDate(),
            now.getHours(),
            now.getMinutes(),
            now.getSeconds(),
            now.getMilliseconds()
        );
    }

    public static FromTicks(ticks: number): DateTime {
        const ms = ticks / 10000; // Convert ticks to milliseconds
        const date = new Date(ms);
        return new DateTime(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate(), date.getUTCHours(), date.getUTCMinutes(), date.getUTCSeconds(), date.getUTCMilliseconds());
    }

    public static Parse(dateString: string): DateTime {
        const date = new Date(dateString);
        if (isNaN(date.getTime())) throw new Error('Invalid date string');
        return new DateTime(
            date.getUTCFullYear(),
            date.getUTCMonth() + 1,
            date.getUTCDate(),
            date.getUTCHours(),
            date.getUTCMinutes(),
            date.getUTCSeconds(),
            date.getUTCMilliseconds()
        );
    }

    // Properties
    public get Year(): number {
        return this._date.getUTCFullYear();
    }

    public get Month(): number {
        return this._date.getUTCMonth() + 1;
    }

    public get Day(): number {
        return this._date.getUTCDate();
    }

    public get Hour(): number {
        return this._date.getUTCHours();
    }

    public get Minute(): number {
        return this._date.getUTCMinutes();
    }

    public get Second(): number {
        return this._date.getUTCSeconds();
    }

    public get Millisecond(): number {
        return this._date.getUTCMilliseconds();
    }

    public get Ticks(): number {
        return this._date.getTime() * 10000;
    }

    public get TimeOfDay(): TimeSpan {
        return new TimeSpan(0, this.Hour, this.Minute, this.Second, this.Millisecond);
    }

    // Methods
    public AddDays(days: number): DateTime {
        const newDate = new Date(this._date);
        newDate.setUTCDate(newDate.getUTCDate() + days);
        return new DateTime(newDate.getUTCFullYear(), newDate.getUTCMonth() + 1, newDate.getUTCDate(), newDate.getUTCHours(), newDate.getUTCMinutes(), newDate.getUTCSeconds(), newDate.getUTCMilliseconds());
    }

    public AddHours(hours: number): DateTime {
        return this.AddMilliseconds(hours * 3600000);
    }

    public AddMinutes(minutes: number): DateTime {
        return this.AddMilliseconds(minutes * 60000);
    }

    public AddSeconds(seconds: number): DateTime {
        return this.AddMilliseconds(seconds * 1000);
    }

    public AddMilliseconds(milliseconds: number): DateTime {
        const newDate = new Date(this._date);
        newDate.setTime(newDate.getTime() + milliseconds);
        return new DateTime(newDate.getUTCFullYear(), newDate.getUTCMonth() + 1, newDate.getUTCDate(), newDate.getUTCHours(), newDate.getUTCMinutes(), newDate.getUTCSeconds(), newDate.getUTCMilliseconds());
    }

    public SubtractDays(days: number): DateTime {
        return this.AddDays(-days);
    }

    public Subtract(ts: TimeSpan): DateTime {
        return this.AddMilliseconds(-ts.TotalMilliseconds);
    }

    public Add(ts: TimeSpan): DateTime {
        return this.AddMilliseconds(ts.TotalMilliseconds);
    }

    public SubtractDateTime(other: DateTime): TimeSpan {
        return new TimeSpan(0, 0, 0, 0, this._date.getTime() - other._date.getTime());
    }

    public ToString(): string {
        return this._date.toISOString();
    }

    public ToLocalTime(): DateTime {
        const localDate = new Date(this._date);
        const offset = localDate.getTimezoneOffset() * 60000; // Get offset in milliseconds
        localDate.setTime(this._date.getTime() - offset);
        return new DateTime(localDate.getFullYear(), localDate.getMonth() + 1, localDate.getDate(), localDate.getHours(), localDate.getMinutes(), localDate.getSeconds(), localDate.getMilliseconds());
    }

    public ToUniversalTime(): DateTime {
        return this;
    }

    // Comparison methods
    public static Compare(dt1: DateTime, dt2: DateTime): number {
        return dt1._date.getTime() - dt2._date.getTime();
    }

    public static Equals(dt1: DateTime, dt2: DateTime): boolean {
        return dt1._date.getTime() === dt2._date.getTime();
    }
}
