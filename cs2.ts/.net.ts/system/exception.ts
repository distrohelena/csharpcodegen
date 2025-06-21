export class Exception {
    private _message;

    public get Message(): string {
        return this._message;
    }

    constructor(message: string) {
        this._message = message;
    }

    public get stackTrace(): string {
        return "";
    }
}