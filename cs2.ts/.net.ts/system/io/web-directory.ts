export class WebDirectory {
    private static storage: Storage = localStorage;

    // Set storage type (localStorage or sessionStorage)
    public static UseLocalStorage(): void {
        this.storage = localStorage;
    }

    public static UseSessionStorage(): void {
        this.storage = sessionStorage;
    }

    // Creates a directory by setting an empty object in storage
    public static CreateDirectory(dirPath: string): void {
        if (!this.Exists(dirPath)) {
            this.storage.setItem(dirPath, JSON.stringify({}));
        }
    }

    // Checks if a directory exists
    public static Exists(dirPath: string): boolean {
        const item = this.storage.getItem(dirPath);
        return item !== null && this.isDirectory(item);
    }

    // Deletes a directory and its contents
    public static Delete(dirPath: string): void {
        if (this.Exists(dirPath)) {
            const directory = JSON.parse(this.storage.getItem(dirPath) as string);
            for (const key in directory) {
                this.storage.removeItem(key);
            }
            this.storage.removeItem(dirPath);
        }
    }

    // Gets all files in the directory (keys inside the directory object)
    public static GetFiles(dirPath: string): string[] {
        if (!this.Exists(dirPath)) {
            return [];
        }
        const directory = JSON.parse(this.storage.getItem(dirPath) as string);
        return Object.keys(directory);
    }

    // Adds a file to the directory
    public static AddFile(dirPath: string, fileName: string, content: string): void {
        if (this.Exists(dirPath)) {
            const directory = JSON.parse(this.storage.getItem(dirPath) as string);
            const filePath = `${dirPath}/${fileName}`;
            this.storage.setItem(filePath, content);
            directory[filePath] = true;
            this.storage.setItem(dirPath, JSON.stringify(directory));
        }
    }

    // Reads a file from the directory
    public static ReadFile(dirPath: string, fileName: string): string | null {
        const filePath = `${dirPath}/${fileName}`;
        return this.storage.getItem(filePath);
    }

    // Deletes a file from the directory
    public static DeleteFile(dirPath: string, fileName: string): void {
        if (this.Exists(dirPath)) {
            const directory = JSON.parse(this.storage.getItem(dirPath) as string);
            const filePath = `${dirPath}/${fileName}`;
            this.storage.removeItem(filePath);
            delete directory[filePath];
            this.storage.setItem(dirPath, JSON.stringify(directory));
        }
    }

    // Checks if a file exists in the directory
    public static FileExists(dirPath: string, fileName: string): boolean {
        const filePath = `${dirPath}/${fileName}`;
        return this.storage.getItem(filePath) !== null;
    }

    // Helper function to check if an item is a directory (i.e., JSON object)
    private static isDirectory(item: string): boolean {
        try {
            const parsedItem = JSON.parse(item);
            return typeof parsedItem === 'object' && parsedItem !== null;
        } catch {
            return false;
        }
    }
}
