// @ts-nocheck
export class AppDomain {
    static CurrentDomain: AppDomain = new AppDomain();

    public get BaseDirectory(): string {
        return process.cwd() + "/";
    }

    private constructor() { }
}
