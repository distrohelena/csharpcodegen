export class Debug {
    // Writes a message to the console
    public static Write(message: any): void {
        console.debug(message);
    }

    // Writes a message followed by a new line to the console
    public static WriteLine(message: any): void {
        console.debug(message);
    }

    // Writes a formatted string with parameters
    public static WriteFormat(format: string, ...args: any[]): void {
        const formattedMessage = Debug.formatString(format, args);
        console.debug(formattedMessage);
    }

    // Asserts a condition, if false, writes the provided message to the console
    public static Assert(condition: boolean, message: string = "Assertion failed"): void {
        if (!condition) {
            console.assert(false, message);
        }
    }

    // Helper function to format a string with arguments
    private static formatString(format: string, args: any[]): string {
        return format.replace(/{(\d+)}/g, (match, number) =>
            typeof args[number] !== 'undefined'
                ? args[number]
                : match
        );
    }
}
