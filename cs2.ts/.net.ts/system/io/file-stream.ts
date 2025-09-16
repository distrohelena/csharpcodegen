import { FileMode } from "./file-mode";
import { MemoryStream } from "./memory-stream";
import { ensureFileBuffer, readFileBuffer, writeFileBuffer } from "./file-storage";

export class FileStream extends MemoryStream {
    private readonly path: string;
    private readonly mode: FileMode;

    constructor(path: string, mode: FileMode) {
        const buffer = readFileBuffer(path);
        super(buffer);
        this.path = path;
        this.mode = mode;
        if (mode === FileMode.Append) {
            this.position = this.length;
        }
    }

    override dispose(): void {
        this.commit();
    }

    override close(): void {
        this.commit();
        super.close();
    }

    override flush(): void {
        this.commit();
    }

    private commit() {
        ensureFileBuffer(this.path, this.mode);
        writeFileBuffer(this.path, this.toArray());
    }
}
