// @ts-nocheck
export class FileNotFoundException extends Error {
    public FileName?: string;

    constructor();
    constructor(message: string);
    constructor(message: string, fileName: string);
    constructor(message?: string | null, fileName?: string | null) {
        const finalMessage = message ?? "Unable to find the specified file.";
        super(finalMessage);
        this.name = "FileNotFoundException";
        this.FileName = fileName ?? undefined;
        Object.setPrototypeOf(this, new.target.prototype);
    }
}
