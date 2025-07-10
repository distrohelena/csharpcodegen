export class JsonConvert {
    static serializeObject(obj: any): string {
        return JSON.stringify(obj);
    }

    static deserializeObject<T>(json: string): T {
        return JSON.parse(json) as T;
    }
}
