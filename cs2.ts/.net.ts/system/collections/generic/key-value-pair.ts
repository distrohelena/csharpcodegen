export class KeyValuePair<T, N> {
    Key: T;
    Value: N;

    constructor(key: T, value: N) {
        this.Key = key;
        this.Value = value;
    }
}