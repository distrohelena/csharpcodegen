export class WebFile {
    private static storage: Storage;

    // Static block for initialization (static constructor simulation)
    static {
        WebFile.storage = localStorage; // Default to localStorage
        console.log("Static block executed: WebFile initialized with localStorage");
    }

    // Set storage type for web: localStorage or sessionStorage
    public static UseLocalStorage(): void {
        this.storage = localStorage;
    }

    public static UseSessionStorage(): void {
        this.storage = sessionStorage;
    }

    // Creates a file (stores in localStorage/sessionStorage)
    public static CreateFile(filePath: string, content: string = ''): void {
        this.storage.setItem(filePath, content);
    }

    // Deletes a file (removes from localStorage/sessionStorage)
    public static DeleteFile(filePath: string): void {
        this.storage.removeItem(filePath);
    }

    // Reads a file's content from localStorage/sessionStorage
    public static ReadFile(filePath: string): string | null {
        return this.storage.getItem(filePath);
    }

    // Checks if a file exists in localStorage/sessionStorage
    public static Exists(filePath: string): boolean {
        return this.storage.getItem(filePath) !== null;
    }

    // Moves a file (copies content and deletes the original)
    public static MoveFile(sourcePath: string, destPath: string): void {
        const content = this.storage.getItem(sourcePath);
        if (content !== null) {
            this.storage.setItem(destPath, content);
            this.storage.removeItem(sourcePath);
        }
    }

    // Appends content to a file (updates the content in localStorage/sessionStorage)
    public static AppendToFile(filePath: string, content: string): void {
        const existingContent = this.storage.getItem(filePath) || '';
        this.storage.setItem(filePath, existingContent + content);
    }
}
