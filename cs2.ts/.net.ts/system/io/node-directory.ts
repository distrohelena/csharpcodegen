// @ts-nocheck
import * as fs from 'fs';
import * as path from 'path';

export class NodeDirectory {
    // Creates a directory at the specified path
    public static CreateDirectory(dirPath: string): void {
        if (!fs.existsSync(dirPath)) {
            fs.mkdirSync(dirPath, { recursive: true });
        }
    }

    // Checks if a directory exists
    public static Exists(dirPath: string): boolean {
        return fs.existsSync(dirPath) && fs.lstatSync(dirPath).isDirectory();
    }

    // Deletes a directory, optionally deleting its contents
    public static Delete(dirPath: string, recursive: boolean = false): void {
        if (this.Exists(dirPath)) {
            fs.rmSync(dirPath, { recursive, force: true });
        }
    }

    // Gets all files in the directory
    public static GetFiles(dirPath: string, searchPattern: string = '*'): string[] {
        if (!this.Exists(dirPath)) {
            return [];
        }
        return fs.readdirSync(dirPath)
            .filter(file => this.matchPattern(file, searchPattern))
            .map(file => path.join(dirPath, file))
            .filter(file => fs.lstatSync(file).isFile());
    }

    // Gets all directories in the directory
    public static GetDirectories(dirPath: string): string[] {
        if (!this.Exists(dirPath)) {
            return [];
        }
        return fs.readdirSync(dirPath)
            .map(file => path.join(dirPath, file))
            .filter(file => fs.lstatSync(file).isDirectory());
    }

    // Moves a directory to a new location
    public static Move(sourceDir: string, destDir: string): void {
        if (this.Exists(sourceDir)) {
            fs.renameSync(sourceDir, destDir);
        }
    }

    // Helper function to match search pattern (only supports wildcard *)
    private static matchPattern(fileName: string, searchPattern: string): boolean {
        if (searchPattern === '*') {
            return true;
        }
        const regexPattern = new RegExp('^' + searchPattern.replace(/\*/g, '.*') + '$');
        return regexPattern.test(fileName);
    }
}
