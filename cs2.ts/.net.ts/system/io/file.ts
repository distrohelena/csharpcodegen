import { FileMode } from "./file-mode";
import { FileStream } from "./file-stream";
import * as fs from "fs";

export class File {
    static exists(filePath: string): boolean {
        return fs.existsSync(filePath);
    }

    static delete(filePath: string) {
        fs.rmSync(filePath);
    }

    static open(filePath: string, fileMode: FileMode): FileStream {
        return new FileStream(filePath, fileMode);
    }
}