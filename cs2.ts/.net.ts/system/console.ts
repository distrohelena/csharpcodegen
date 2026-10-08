// @ts-nocheck

export class Console {
    /** Browser error stream for diagnostics emitted through Console.Error in managed code. */
    static readonly Error = {
        /** Routes a diagnostic fragment to the browser's error console. */
        Write(line: unknown): void { console.error(line); },
        /** Routes a complete diagnostic line to the browser's error console. */
        WriteLine(line: unknown): void { console.error(line); }
    };
    static Write(line: string | number | any) {
        console.log(line);
    }

    static WriteLine(line: string | number | any) {
        console.log(line);
    }
}
