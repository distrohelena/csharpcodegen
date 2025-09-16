import { FileMode } from "./file-mode";
import { FileStream } from "./node-file-stream";
import * as fs from "fs";

export class File {
    static exists(filePath: string): boolean {
        return fs.existsSync(filePath);
    }

    static delete(filePath: string) {
        if (fs.existsSync(filePath)) {
            fs.rmSync(filePath);
        }
    }

    static open(filePath: string, fileMode: FileMode): FileStream {
        return new FileStream(filePath, fileMode);
    }

    static openRead(filePath: string): FileStream {
        return new FileStream(filePath, FileMode.Open);
    }

    static openWrite(filePath: string): FileStream {
        return new FileStream(filePath, FileMode.OpenOrCreate);
    }
}
