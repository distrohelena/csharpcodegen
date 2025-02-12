import { FileMode } from "./file-mode";
import { FileStream } from "./file-stream";

export class File {
    static Open(filePath: string, fileMode: FileMode): FileStream {
        return new FileStream(filePath, fileMode);
    }
}