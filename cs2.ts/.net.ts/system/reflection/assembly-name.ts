import { Version } from "../version";

export class AssemblyName {
    get Version(): Version {
        return new Version();
    }
}