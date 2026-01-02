// @ts-nocheck
export class Environment {

    public static get processorCount(): number {
        return 1;
    }

    public static GetFolderPath(folder: Environment.SpecialFolder): string {
        if (typeof process === "undefined" || !process || !process.env) {
            throw new Error("Environment.GetFolderPath is not supported in this runtime.");
        }

        const env = process.env;
        const home = env.USERPROFILE || env.HOME || "";
        let resolved = "";

        switch (folder) {
            case Environment.SpecialFolder.ApplicationData:
                resolved = env.APPDATA || env.XDG_DATA_HOME || (home ? `${home}/.local/share` : "");
                break;
            case Environment.SpecialFolder.LocalApplicationData:
                resolved = env.LOCALAPPDATA || env.XDG_DATA_HOME || env.APPDATA || (home ? `${home}/.local/share` : "");
                break;
            case Environment.SpecialFolder.MyDocuments:
                resolved = env.DOCUMENTS || (home ? `${home}/Documents` : "");
                break;
            default:
                resolved = "";
                break;
        }

        if (!resolved) {
            throw new Error(`Unable to resolve folder path for ${folder}.`);
        }

        return resolved;
    }
}

export namespace Environment {
    export enum SpecialFolder {
        MyDocuments = 5,
        ApplicationData = 26,
        LocalApplicationData = 28
    }
}
