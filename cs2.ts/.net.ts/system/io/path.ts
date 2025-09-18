// @ts-nocheck
export class Path {
    // Directory separator characters
    private static readonly DirectorySeparatorChar = '/';
    private static readonly AltDirectorySeparatorChar = '\\';
    private static readonly VolumeSeparatorChar = ':';
    private static readonly PathSeparator = ';';

    // Invalid characters for file names
    private static readonly InvalidFileNameChars = [
        '<', '>', ':', '"', '|', '?', '*', '\0'
    ];

    // Invalid characters for path names
    private static readonly InvalidPathChars = [
        '<', '>', ':', '"', '|', '?', '*', '\0'
    ];

    /**
     * Combines two path strings
     */
    static combine(path1: string, path2: string): string {
        if (!path1 || !path2) {
            return path1 || path2 || '';
        }

        // Normalize separators
        const normalizedPath1 = this.normalizeSeparators(path1);
        const normalizedPath2 = this.normalizeSeparators(path2);

        // If path2 is absolute, return it
        if (this.isPathRooted(normalizedPath2)) {
            return normalizedPath2;
        }

        // Remove trailing separator from path1 if it exists
        const trimmedPath1 = normalizedPath1.endsWith(this.DirectorySeparatorChar) 
            ? normalizedPath1.slice(0, -1) 
            : normalizedPath1;

        // Remove leading separator from path2 if it exists
        const trimmedPath2 = normalizedPath2.startsWith(this.DirectorySeparatorChar) 
            ? normalizedPath2.slice(1) 
            : normalizedPath2;

        return trimmedPath1 + this.DirectorySeparatorChar + trimmedPath2;
    }

    /**
     * Combines multiple path strings
     */
    static combineMultiple(...paths: string[]): string {
        if (paths.length === 0) return '';
        if (paths.length === 1) return paths[0];

        let result = paths[0];
        for (let i = 1; i < paths.length; i++) {
            result = this.combine(result, paths[i]);
        }
        return result;
    }

    /**
     * Gets the directory name for the specified path
     */
    static getDirectoryName(path: string): string | null {
        if (!path || path.trim() === '') return null;

        const normalizedPath = this.normalizeSeparators(path);
        
        // Handle root paths
        if (normalizedPath === this.DirectorySeparatorChar || 
            normalizedPath === this.AltDirectorySeparatorChar) {
            return null;
        }

        // Handle paths with volume separator (Windows-style)
        if (normalizedPath.includes(this.VolumeSeparatorChar)) {
            const parts = normalizedPath.split(this.VolumeSeparatorChar);
            if (parts.length >= 2) {
                const volume = parts[0] + this.VolumeSeparatorChar;
                const rest = parts.slice(1).join(this.VolumeSeparatorChar);
                if (rest === '' || rest === this.DirectorySeparatorChar) {
                    return volume;
                }
            }
        }

        const lastSeparatorIndex = normalizedPath.lastIndexOf(this.DirectorySeparatorChar);
        if (lastSeparatorIndex === -1) {
            return '';
        }

        if (lastSeparatorIndex === 0) {
            return this.DirectorySeparatorChar;
        }

        return normalizedPath.substring(0, lastSeparatorIndex);
    }

    /**
     * Gets the file name and extension of the specified path
     */
    static getFileName(path: string): string {
        if (!path || path.trim() === '') return '';

        const normalizedPath = this.normalizeSeparators(path);
        const lastSeparatorIndex = normalizedPath.lastIndexOf(this.DirectorySeparatorChar);
        
        if (lastSeparatorIndex === -1) {
            return normalizedPath;
        }

        return normalizedPath.substring(lastSeparatorIndex + 1);
    }

    /**
     * Gets the file name of the specified path without the extension
     */
    static getFileNameWithoutExtension(path: string): string {
        const fileName = this.getFileName(path);
        const extension = this.getExtension(fileName);
        return fileName.substring(0, fileName.length - extension.length);
    }

    /**
     * Gets the extension of the specified path
     */
    static getExtension(path: string): string {
        if (!path || path.trim() === '') return '';

        const fileName = this.getFileName(path);
        const lastDotIndex = fileName.lastIndexOf('.');
        
        if (lastDotIndex === -1 || lastDotIndex === fileName.length - 1) {
            return '';
        }

        return fileName.substring(lastDotIndex);
    }

    /**
     * Gets the root directory information of the specified path
     */
    static getPathRoot(path: string): string | null {
        if (!path || path.trim() === '') return null;

        const normalizedPath = this.normalizeSeparators(path);

        // Handle absolute paths starting with separator
        if (normalizedPath.startsWith(this.DirectorySeparatorChar)) {
            return this.DirectorySeparatorChar;
        }

        // Handle Windows-style paths with volume separator
        if (normalizedPath.includes(this.VolumeSeparatorChar)) {
            const parts = normalizedPath.split(this.VolumeSeparatorChar);
            if (parts.length >= 2) {
                const volume = parts[0] + this.VolumeSeparatorChar;
                const rest = parts.slice(1).join(this.VolumeSeparatorChar);
                if (rest.startsWith(this.DirectorySeparatorChar)) {
                    return volume + this.DirectorySeparatorChar;
                }
                return volume;
            }
        }

        return null;
    }

    /**
     * Returns a random folder name or file name
     */
    static getRandomFileName(): string {
        const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
        let result = '';
        for (let i = 0; i < 8; i++) {
            result += chars.charAt(Math.floor(Math.random() * chars.length));
        }
        result += '.tmp';
        return result;
    }

    /**
     * Gets a temporary file name
     */
    static getTempFileName(): string {
        return this.combine(this.getTempPath(), this.getRandomFileName());
    }

    /**
     * Gets the path of the current user's temporary folder
     */
    static getTempPath(): string {
        // In browser environment, we'll use a virtual temp path
        // In Node.js, this would typically return os.tmpdir()
        return '/tmp';
    }

    /**
     * Determines whether a path includes a file name extension
     */
    static hasExtension(path: string): boolean {
        return this.getExtension(path) !== '';
    }

    /**
     * Determines whether the specified path is rooted
     */
    static isPathRooted(path: string): boolean {
        if (!path || path.trim() === '') return false;

        const normalizedPath = this.normalizeSeparators(path);
        
        // Check for absolute paths starting with separator
        if (normalizedPath.startsWith(this.DirectorySeparatorChar)) {
            return true;
        }

        // Check for Windows-style paths with volume separator
        if (normalizedPath.includes(this.VolumeSeparatorChar)) {
            const parts = normalizedPath.split(this.VolumeSeparatorChar);
            if (parts.length >= 2) {
                const rest = parts.slice(1).join(this.VolumeSeparatorChar);
                return rest.startsWith(this.DirectorySeparatorChar);
            }
        }

        return false;
    }

    /**
     * Changes the extension of a path string
     */
    static changeExtension(path: string, extension: string | null): string {
        if (!path || path.trim() === '') return path;

        const currentExtension = this.getExtension(path);
        const pathWithoutExtension = path.substring(0, path.length - currentExtension.length);
        
        if (extension === null || extension === '') {
            return pathWithoutExtension;
        }

        // Ensure extension starts with a dot
        const normalizedExtension = extension.startsWith('.') ? extension : '.' + extension;
        return pathWithoutExtension + normalizedExtension;
    }

    /**
     * Normalizes path separators to the standard directory separator
     */
    private static normalizeSeparators(path: string): string {
        return path.replace(new RegExp('\\' + this.AltDirectorySeparatorChar, 'g'), this.DirectorySeparatorChar);
    }

    /**
     * Gets the full path for the specified path
     */
    static getFullPath(path: string): string {
        if (!path || path.trim() === '') {
            throw new Error('Path cannot be null or empty');
        }

        const normalizedPath = this.normalizeSeparators(path);
        
        // If already absolute, return as is
        if (this.isPathRooted(normalizedPath)) {
            return normalizedPath;
        }

        // For relative paths, we'll assume they're relative to current directory
        // In a real implementation, this would resolve against the current working directory
        return this.combine('.', normalizedPath);
    }

    /**
     * Gets the absolute path for the specified path
     */
    static getAbsolutePath(path: string): string {
        return this.getFullPath(path);
    }

    /**
     * Gets the relative path from one path to another
     */
    static getRelativePath(relativeTo: string, path: string): string {
        if (!relativeTo || !path) {
            throw new Error('Both paths must be provided');
        }

        const normalizedRelativeTo = this.normalizeSeparators(relativeTo);
        const normalizedPath = this.normalizeSeparators(path);

        // Simple implementation - in a real scenario, this would be more complex
        // to handle various path combinations
        if (normalizedPath.startsWith(normalizedRelativeTo)) {
            const relativePart = normalizedPath.substring(normalizedRelativeTo.length);
            return relativePart.startsWith(this.DirectorySeparatorChar) 
                ? relativePart.substring(1) 
                : relativePart;
        }

        return normalizedPath;
    }

    /**
     * Joins path segments
     */
    static join(...paths: string[]): string {
        return this.combineMultiple(...paths);
    }

    /**
     * Resolves a path
     */
    static resolve(...paths: string[]): string {
        return this.getFullPath(this.combineMultiple(...paths));
    }

    /**
     * Normalizes a path
     */
    static normalize(path: string): string {
        if (!path || path.trim() === '') return path;

        const normalizedPath = this.normalizeSeparators(path);
        const segments = normalizedPath.split(this.DirectorySeparatorChar);
        const result: string[] = [];

        for (const segment of segments) {
            if (segment === '' || segment === '.') {
                continue;
            }
            if (segment === '..') {
                if (result.length > 0) {
                    result.pop();
                }
                continue;
            }
            result.push(segment);
        }

        return result.join(this.DirectorySeparatorChar);
    }
} 