// @ts-nocheck
import { Stream } from "../../io/stream";
import { JsonWriterOptions } from "./json-writer-options";

type WriterContext = {
    type: "object" | "array";
    first: boolean;
};

export class Utf8JsonWriter {
    private readonly parts: string[] = [];
    private readonly stack: WriterContext[] = [];
    private pendingProperty: boolean = false;
    private indentLevel: number = 0;
    private readonly indented: boolean;
    private readonly stream?: Stream;
    private flushed: boolean = false;

    constructor(stream?: Stream, options?: JsonWriterOptions) {
        this.stream = stream;
        this.indented = !!options?.Indented;
    }

    private writeIndent(): void {
        if (!this.indented) {
            return;
        }
        this.parts.push("\n" + "  ".repeat(this.indentLevel));
    }

    private writeValuePrefix(): void {
        if (this.stack.length === 0) {
            return;
        }
        const ctx = this.stack[this.stack.length - 1];
        if (ctx.type === "array") {
            if (!ctx.first) {
                this.parts.push(",");
            } else {
                ctx.first = false;
            }
            this.writeIndent();
        } else if (ctx.type === "object") {
            if (!this.pendingProperty) {
                if (!ctx.first) {
                    this.parts.push(",");
                } else {
                    ctx.first = false;
                }
                this.writeIndent();
            }
        }
    }

    public WriteStartObject(): void {
        if (this.pendingProperty) {
            this.pendingProperty = false;
        } else {
            this.writeValuePrefix();
        }
        this.parts.push("{");
        this.stack.push({ type: "object", first: true });
        this.indentLevel++;
    }

    public WriteEndObject(): void {
        const ctx = this.stack.pop();
        this.indentLevel = Math.max(this.indentLevel - 1, 0);
        if (this.indented && ctx && !ctx.first) {
            this.writeIndent();
        }
        this.parts.push("}");
    }

    public WriteStartArray(): void {
        if (this.pendingProperty) {
            this.pendingProperty = false;
        } else {
            this.writeValuePrefix();
        }
        this.parts.push("[");
        this.stack.push({ type: "array", first: true });
        this.indentLevel++;
    }

    public WriteEndArray(): void {
        const ctx = this.stack.pop();
        this.indentLevel = Math.max(this.indentLevel - 1, 0);
        if (this.indented && ctx && !ctx.first) {
            this.writeIndent();
        }
        this.parts.push("]");
    }

    public WritePropertyName(name: string): void {
        const ctx = this.stack[this.stack.length - 1];
        if (!ctx || ctx.type !== "object") {
            throw new Error("WritePropertyName must be called inside an object.");
        }
        if (!ctx.first) {
            this.parts.push(",");
        } else {
            ctx.first = false;
        }
        this.writeIndent();
        this.parts.push(JSON.stringify(name));
        this.parts.push(":");
        if (this.indented) {
            this.parts.push(" ");
        }
        this.pendingProperty = true;
    }

    public WriteString(nameOrValue: string, value?: string): void {
        if (value === undefined) {
            this.WriteStringValue(nameOrValue);
            return;
        }
        this.WritePropertyName(nameOrValue);
        this.WriteStringValue(value);
    }

    public WriteStringValue(value: any): void {
        if (value == null) {
            this.WriteNullValue();
            return;
        }
        if (this.pendingProperty) {
            this.pendingProperty = false;
        } else {
            this.writeValuePrefix();
        }
        const text = typeof value === "string"
            ? value
            : typeof value?.ToString === "function"
                ? value.ToString()
                : value.toString();
        this.parts.push(JSON.stringify(text));
    }

    public WriteNumber(nameOrValue: string | number, value?: number): void {
        if (value === undefined) {
            this.WriteNumberValue(nameOrValue as number);
            return;
        }
        this.WritePropertyName(String(nameOrValue));
        this.WriteNumberValue(value);
    }

    public WriteNumberValue(value: number): void {
        if (this.pendingProperty) {
            this.pendingProperty = false;
        } else {
            this.writeValuePrefix();
        }
        this.parts.push(Number.isFinite(value) ? String(value) : "null");
    }

    public WriteBoolean(nameOrValue: string | boolean, value?: boolean): void {
        if (value === undefined) {
            this.WriteBooleanValue(nameOrValue as boolean);
            return;
        }
        this.WritePropertyName(String(nameOrValue));
        this.WriteBooleanValue(value);
    }

    public WriteBooleanValue(value: boolean): void {
        if (this.pendingProperty) {
            this.pendingProperty = false;
        } else {
            this.writeValuePrefix();
        }
        this.parts.push(value ? "true" : "false");
    }

    public WriteNullValue(): void {
        if (this.pendingProperty) {
            this.pendingProperty = false;
        } else {
            this.writeValuePrefix();
        }
        this.parts.push("null");
    }

    public Flush(): void {
        if (!this.stream || this.flushed) {
            return;
        }
        const text = this.toString();
        const encoder = new TextEncoder();
        const bytes = encoder.encode(text);
        this.stream.write(bytes, 0, bytes.length);
        this.flushed = true;
    }

    public dispose(): void {
        this.Flush();
    }

    public toString(): string {
        return this.parts.join("");
    }
}
