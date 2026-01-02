// @ts-nocheck
import { List } from "../../collections/generic/list";
import { JsonIgnoreCondition } from "./json-ignore-condition";
import { JsonNumberHandling } from "./json-number-handling";
import { JsonConverter } from "./serialization/json-converter";

export class JsonConverterCollection<T> extends List<T> {
    public Add(item: T): void {
        this.add(item);
    }
}

export class JsonSerializerOptions {
    public AllowTrailingCommas: boolean = false;
    public PropertyNameCaseInsensitive: boolean = false;
    public PropertyNamingPolicy: any = null;
    public WriteIndented: boolean = false;
    public DefaultIgnoreCondition: JsonIgnoreCondition = JsonIgnoreCondition.Never;
    public NumberHandling: JsonNumberHandling = JsonNumberHandling.Strict;
    public Converters: JsonConverterCollection<JsonConverter> = new JsonConverterCollection<JsonConverter>();
}
