export class KeyValuePair<T, N> {
    key: T;
    value: N;

    constructor(key: T, value: N) {
        this.key = key;
        this.value = value;
    }
}