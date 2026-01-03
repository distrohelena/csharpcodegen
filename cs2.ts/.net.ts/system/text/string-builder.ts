// @ts-nocheck
export class StringBuilder {
    private parts: string[] = [];
    private _length: number = 0;
    private _capacity: number = 0;

    static NewLine: string = "\n";

    constructor();
    constructor(capacity: number);
    constructor(value: string);
    constructor(value: string, capacity: number);
    constructor(arg1?: string | number, arg2?: number) {
        if (typeof arg1 === "number") {
            this._capacity = arg1;
        } else if (arg1 !== undefined && arg1 !== null) {
            const text = String(arg1);
            this.parts.push(text);
            this._length = text.length;
        }

        if (typeof arg2 === "number") {
            this._capacity = Math.max(this._capacity, arg2);
        }
    }

    get Length(): number {
        return this._length;
    }

    set Length(value: number) {
        if (value < 0) {
            throw new Error("Length must be non-negative.");
        }

        const current = this.ToString();
        if (value <= current.length) {
            const truncated = current.substring(0, value);
            this.parts = truncated.length > 0 ? [truncated] : [];
            this._length = value;
            return;
        }

        const padding = "\0".repeat(value - current.length);
        this.parts = current.length > 0 ? [current, padding] : [padding];
        this._length = value;
    }

    get Capacity(): number {
        return Math.max(this._capacity, this._length);
    }

    set Capacity(value: number) {
        if (value < this._length) {
            throw new Error("Capacity must be >= Length.");
        }
        this._capacity = value;
    }

    Append(value?: any): StringBuilder {
        if (value === null || value === undefined) {
            return this;
        }
        const text = String(value);
        if (text.length === 0) {
            return this;
        }
        this.parts.push(text);
        this._length += text.length;
        return this;
    }

    AppendLine(value?: any): StringBuilder {
        if (value !== undefined && value !== null) {
            this.Append(value);
        }
        return this.Append(StringBuilder.NewLine);
    }

    Clear(): StringBuilder {
        this.parts = [];
        this._length = 0;
        return this;
    }

    Insert(index: number, value: any): StringBuilder {
        const current = this.ToString();
        if (index < 0 || index > current.length) {
            throw new Error("Index out of range.");
        }
        const insertText = value === null || value === undefined ? "" : String(value);
        const result = current.slice(0, index) + insertText + current.slice(index);
        this.parts = result.length > 0 ? [result] : [];
        this._length = result.length;
        return this;
    }

    Remove(startIndex: number, length: number): StringBuilder {
        const current = this.ToString();
        if (startIndex < 0 || length < 0 || startIndex + length > current.length) {
            throw new Error("Index and length must be within the bounds of the StringBuilder.");
        }
        const result = current.slice(0, startIndex) + current.slice(startIndex + length);
        this.parts = result.length > 0 ? [result] : [];
        this._length = result.length;
        return this;
    }

    Replace(oldValue: string, newValue: string): StringBuilder {
        if (oldValue === null || oldValue === undefined || oldValue === "") {
            return this;
        }
        const replacement = newValue === null || newValue === undefined ? "" : String(newValue);
        const current = this.ToString();
        const result = current.split(oldValue).join(replacement);
        this.parts = result.length > 0 ? [result] : [];
        this._length = result.length;
        return this;
    }

    AppendFormat(format: string, ...args: any[]): StringBuilder {
        if (format === null || format === undefined) {
            return this;
        }
        const formatted = String(format).replace(/\{(\d+)(:[^}]+)?\}/g, (match, index) => {
            const argIndex = Number(index);
            if (!Number.isFinite(argIndex) || argIndex < 0 || argIndex >= args.length) {
                return match;
            }
            const arg = args[argIndex];
            return arg === null || arg === undefined ? "" : String(arg);
        });
        return this.Append(formatted);
    }

    ToString(): string {
        return this.parts.join("");
    }

    toString(): string {
        return this.ToString();
    }
}
