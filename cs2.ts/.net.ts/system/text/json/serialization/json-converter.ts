// @ts-nocheck
export class JsonConverter<T = any> {
    public CanConvert(_typeToConvert: any): boolean {
        return false;
    }

    public Read(_reader: any, _typeToConvert: any, _options: any): T {
        throw new Error("JsonConverter.Read must be implemented by subclasses.");
    }

    public Write(_writer: any, _value: T, _options: any): void {
        throw new Error("JsonConverter.Write must be implemented by subclasses.");
    }
}
