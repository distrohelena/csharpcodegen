// @ts-nocheck
import { JsonElement } from "./json-element";

export class JsonProperty {
    public Name: string;
    public Value: JsonElement;

    constructor(name: string, value: any) {
        this.Name = name;
        this.Value = value instanceof JsonElement ? value : new JsonElement(value);
    }
}
