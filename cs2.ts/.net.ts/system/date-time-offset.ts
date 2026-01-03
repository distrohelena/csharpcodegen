// @ts-nocheck
import { DateTime } from "./date-time";
import { TimeSpan } from "./time-span";

export class DateTimeOffset {
    private _utcDate: Date;
    private _offsetMinutes: number;

    constructor();
    constructor(dateTime: DateTime);
    constructor(dateTime: DateTime, offset: TimeSpan);
    constructor(ticks: number, offset: TimeSpan);
    constructor(year: number, month: number, day: number, hours?: number, minutes?: number, seconds?: number, milliseconds?: number, offset?: TimeSpan);
    constructor(arg1?: any, arg2?: any, arg3?: any, arg4?: any, arg5?: any, arg6?: any, arg7?: any, arg8?: any) {
        if (arg1 instanceof DateTime) {
            const offset = arg2 instanceof TimeSpan ? arg2 : TimeSpan.fromMinutes(0);
            this._offsetMinutes = offset.TotalMinutes;
            const ms = arg1.Ticks / 10000;
            this._utcDate = new Date(ms);
            return;
        }

        if (typeof arg1 === "number" && arg2 instanceof TimeSpan && arguments.length === 2) {
            this._offsetMinutes = arg2.TotalMinutes;
            const ms = arg1 / 10000;
            this._utcDate = new Date(ms);
            return;
        }

        if (typeof arg1 === "number") {
            const year = arg1;
            const month = arg2 ?? 1;
            const day = arg3 ?? 1;
            const hours = arg4 ?? 0;
            const minutes = arg5 ?? 0;
            const seconds = arg6 ?? 0;
            const milliseconds = arg7 ?? 0;
            const offset = arg8 instanceof TimeSpan ? arg8 : TimeSpan.fromMinutes(0);
            this._offsetMinutes = offset.TotalMinutes;
            const localMs = Date.UTC(year, month - 1, day, hours, minutes, seconds, milliseconds);
            this._utcDate = new Date(localMs - this._offsetMinutes * 60000);
            return;
        }

        this._offsetMinutes = 0;
        this._utcDate = new Date(0);
    }

    public static get UtcNow(): DateTimeOffset {
        return DateTimeOffset.FromUnixTimeMilliseconds(Date.now());
    }

    public static get Now(): DateTimeOffset {
        const now = new Date();
        const offsetMinutes = -now.getTimezoneOffset();
        return DateTimeOffset.FromUnixTimeMilliseconds(now.getTime()).ToOffset(TimeSpan.fromMinutes(offsetMinutes));
    }

    public static FromUnixTimeSeconds(seconds: number): DateTimeOffset {
        return DateTimeOffset.FromUnixTimeMilliseconds(seconds * 1000);
    }

    public static FromUnixTimeMilliseconds(milliseconds: number): DateTimeOffset {
        const instance = new DateTimeOffset();
        instance._utcDate = new Date(milliseconds);
        instance._offsetMinutes = 0;
        return instance;
    }

    public static Parse(value: string): DateTimeOffset {
        if (!value) {
            throw new Error("Invalid date string");
        }
        const parsed = new Date(value);
        if (isNaN(parsed.getTime())) {
            throw new Error("Invalid date string");
        }

        const offsetMinutes = DateTimeOffset.parseOffsetMinutes(value);
        const instance = new DateTimeOffset();
        instance._utcDate = new Date(parsed.getTime());
        instance._offsetMinutes = offsetMinutes;
        return instance;
    }

    public get Year(): number {
        return this.getLocalDate().getUTCFullYear();
    }

    public get Month(): number {
        return this.getLocalDate().getUTCMonth() + 1;
    }

    public get Day(): number {
        return this.getLocalDate().getUTCDate();
    }

    public get Hour(): number {
        return this.getLocalDate().getUTCHours();
    }

    public get Minute(): number {
        return this.getLocalDate().getUTCMinutes();
    }

    public get Second(): number {
        return this.getLocalDate().getUTCSeconds();
    }

    public get Millisecond(): number {
        return this.getLocalDate().getUTCMilliseconds();
    }

    public get Offset(): TimeSpan {
        return TimeSpan.fromMinutes(this._offsetMinutes);
    }

    public get UtcDateTime(): DateTime {
        return new DateTime(
            this._utcDate.getUTCFullYear(),
            this._utcDate.getUTCMonth() + 1,
            this._utcDate.getUTCDate(),
            this._utcDate.getUTCHours(),
            this._utcDate.getUTCMinutes(),
            this._utcDate.getUTCSeconds(),
            this._utcDate.getUTCMilliseconds()
        );
    }

    public get DateTime(): DateTime {
        const local = this.getLocalDate();
        return new DateTime(
            local.getUTCFullYear(),
            local.getUTCMonth() + 1,
            local.getUTCDate(),
            local.getUTCHours(),
            local.getUTCMinutes(),
            local.getUTCSeconds(),
            local.getUTCMilliseconds()
        );
    }

    public get Ticks(): number {
        const localMs = this._utcDate.getTime() + this._offsetMinutes * 60000;
        return localMs * 10000;
    }

    public get UtcTicks(): number {
        return this._utcDate.getTime() * 10000;
    }

    public ToUnixTimeSeconds(): number {
        return Math.trunc(this._utcDate.getTime() / 1000);
    }

    public ToUnixTimeMilliseconds(): number {
        return this._utcDate.getTime();
    }

    public ToUniversalTime(): DateTimeOffset {
        return this.ToOffset(TimeSpan.fromMinutes(0));
    }

    public ToOffset(offset: TimeSpan): DateTimeOffset {
        const instance = new DateTimeOffset();
        instance._utcDate = new Date(this._utcDate.getTime());
        instance._offsetMinutes = offset.TotalMinutes;
        return instance;
    }

    public Add(ts: TimeSpan): DateTimeOffset {
        const instance = new DateTimeOffset();
        instance._utcDate = new Date(this._utcDate.getTime() + ts.TotalMilliseconds);
        instance._offsetMinutes = this._offsetMinutes;
        return instance;
    }

    public AddDays(days: number): DateTimeOffset {
        return this.Add(TimeSpan.fromDays(days));
    }

    public AddHours(hours: number): DateTimeOffset {
        return this.Add(TimeSpan.fromHours(hours));
    }

    public AddMinutes(minutes: number): DateTimeOffset {
        return this.Add(TimeSpan.fromMinutes(minutes));
    }

    public AddSeconds(seconds: number): DateTimeOffset {
        return this.Add(TimeSpan.fromSeconds(seconds));
    }

    public AddMilliseconds(milliseconds: number): DateTimeOffset {
        return this.Add(TimeSpan.fromMilliseconds(milliseconds));
    }

    public Subtract(ts: TimeSpan): DateTimeOffset {
        return this.Add(TimeSpan.fromMilliseconds(-ts.TotalMilliseconds));
    }

    public ToString(format?: string): string {
        if (format === "o" || format === "O" || !format) {
            const local = this.getLocalDate();
            const year = local.getUTCFullYear().toString().padStart(4, "0");
            const month = (local.getUTCMonth() + 1).toString().padStart(2, "0");
            const day = local.getUTCDate().toString().padStart(2, "0");
            const hours = local.getUTCHours().toString().padStart(2, "0");
            const minutes = local.getUTCMinutes().toString().padStart(2, "0");
            const seconds = local.getUTCSeconds().toString().padStart(2, "0");
            const millis = local.getUTCMilliseconds().toString().padStart(3, "0");
            const fractional = `${millis}0000`;
            const offset = DateTimeOffset.formatOffset(this._offsetMinutes);
            return `${year}-${month}-${day}T${hours}:${minutes}:${seconds}.${fractional}${offset}`;
        }
        return this.ToString("O");
    }

    public static Compare(left: DateTimeOffset, right: DateTimeOffset): number {
        return left._utcDate.getTime() - right._utcDate.getTime();
    }

    public static Equals(left: DateTimeOffset, right: DateTimeOffset): boolean {
        return left._utcDate.getTime() === right._utcDate.getTime() && left._offsetMinutes === right._offsetMinutes;
    }

    private getLocalDate(): Date {
        return new Date(this._utcDate.getTime() + this._offsetMinutes * 60000);
    }

    private static formatOffset(offsetMinutes: number): string {
        if (offsetMinutes === 0) {
            return "+00:00";
        }
        const sign = offsetMinutes >= 0 ? "+" : "-";
        const total = Math.abs(offsetMinutes);
        const hours = Math.floor(total / 60).toString().padStart(2, "0");
        const minutes = (total % 60).toString().padStart(2, "0");
        return `${sign}${hours}:${minutes}`;
    }

    private static parseOffsetMinutes(value: string): number {
        if (!value) {
            return 0;
        }
        const trimmed = value.trim();
        if (trimmed.endsWith("Z") || trimmed.endsWith("z")) {
            return 0;
        }
        const match = trimmed.match(/([+-])(\d{2}):?(\d{2})$/);
        if (!match) {
            return 0;
        }
        const sign = match[1] === "-" ? -1 : 1;
        const hours = parseInt(match[2], 10);
        const minutes = parseInt(match[3], 10);
        return sign * (hours * 60 + minutes);
    }
}
