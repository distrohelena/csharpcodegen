// @ts-nocheck
import { Version } from "../version";
import { AssemblyName } from "./assembly-name";

export class Assembly {
    private static entryAssembly: Assembly;

    public name: string;
    public version: string;
    public description: string;
    private location: string;

    static {
        Assembly.SetEntryAssembly(
            new Assembly("$ASSEMBLY_NAME$", "$ASSEMBLY_VERSION$", "$ASSEMBLY_DESCRIPTION$")
        );
    }

    // Constructor to initialize an assembly with a name and optional metadata
    constructor(name: string, version: string = "1.0.0", description: string, location: string = "") {
        this.name = name;
        this.version = version;
        this.description = description;
        this.location = location;
    }

    // Sets the entry assembly manually
    public static SetEntryAssembly(assembly: Assembly): void {
        Assembly.entryAssembly = assembly;
    }

    // Gets the entry assembly (throws an error if not set)
    public static GetEntryAssembly(): Assembly {
        if (!Assembly.entryAssembly) {
            throw new Error("Entry assembly not set. Use SetEntryAssembly() to initialize it.");
        }
        return Assembly.entryAssembly;
    }

    // Creates a custom assembly manually
    public static CreateAssembly(name: string, version: string = "1.0.0", description: string, location: string = ""): Assembly {
        return new Assembly(name, version, description, location);
    }

    // Retrieves metadata about the assembly
    public GetName(): AssemblyName {
        return new AssemblyName();
    }

    public GetVersion(): string {
        return this.version;
    }

    public GetDescription(): string {
        return this.description;
    }

    public get Location(): string {
        return this.location;
    }
}
