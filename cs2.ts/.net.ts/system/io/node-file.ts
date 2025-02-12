import * as fs from 'fs';
import * as path from 'path';

export class NodeFile {
    // Creates a file in Node.js
    public static CreateFile(filePath: string, content: string = ''): void {
        fs.writeFileSync(filePath, content);
    }

    // Deletes a file in Node.js
    public static DeleteFile(filePath: string): void {
        if (fs.existsSync(filePath)) {
            fs.unlinkSync(filePath);
        }
    }

    // Reads a file's content in Node.js
    public static ReadFile(filePath: string): string | null {
        if (fs.existsSync(filePath)) {
            return fs.readFileSync(filePath, 'utf8');
        } else {
            return null;
        }
    }

    // Checks if a file exists in Node.js
    public static Exists(filePath: string): boolean {
        return fs.existsSync(filePath);
    }

    // Moves a file in Node.js
    public static MoveFile(sourcePath: string, destPath: string): void {
        if (fs.existsSync(sourcePath)) {
            fs.renameSync(sourcePath, destPath);
        }
    }

    // Appends content to a file in Node.js
    public static AppendToFile(filePath: string, content: string): void {
        fs.appendFileSync(filePath, content);
    }
}
