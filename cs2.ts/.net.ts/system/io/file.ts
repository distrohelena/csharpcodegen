// @ts-nocheck
import { FileMode } from "./file-mode";
import { FileStream } from "./file-stream";
import { deleteFileBuffer, ensureFileBuffer, fileExists } from "./file-storage";

export class File {
    static exists(filePath: string): boolean {
        return fileExists(filePath);
    }

    static delete(filePath: string) {
        deleteFileBuffer(filePath);
    }

    static open(filePath: string, fileMode: FileMode): FileStream {
        ensureFileBuffer(filePath, fileMode);
        return new FileStream(filePath, fileMode);
    }

    static openRead(filePath: string): FileStream {
        ensureFileBuffer(filePath, FileMode.Open);
        return new FileStream(filePath, FileMode.Open);
    }

    static openWrite(filePath: string): FileStream {
        ensureFileBuffer(filePath, FileMode.OpenOrCreate);
        return new FileStream(filePath, FileMode.OpenOrCreate);
    }
}
