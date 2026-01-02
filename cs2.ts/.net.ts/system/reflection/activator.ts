// @ts-nocheck
import { BindingFlags, Type } from "../../src/reflection";

export class Activator {
    public static CreateInstance(type: Type, ...args: any[]): any {
        if (!type) {
            throw new Error("type");
        }

        if (args.length === 1 && Array.isArray(args[0])) {
            args = args[0];
        }

        const ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (!ctors || ctors.length === 0 || !(type as any)._ctor) {
            return Activator.getDefaultValue(type);
        }

        let ctor = ctors.find(c => c.parameters.length === args.length);
        if (!ctor) {
            ctor = ctors[0];
        }

        return ctor.Invoke(args);
    }

    private static getDefaultValue(type: Type): any {
        if (type.IsEnum) {
            return 0;
        }

        const fullName = type.fullName ?? type.FullName ?? "";
        switch (fullName) {
            case "System.Boolean":
                return false;
            case "System.String":
                return "";
            case "System.Char":
                return "\u0000";
        }

        if (fullName.indexOf("System.") === 0) {
            if (fullName.indexOf("Int") >= 0 || fullName.indexOf("UInt") >= 0 || fullName.indexOf("Single") >= 0 || fullName.indexOf("Double") >= 0 || fullName.indexOf("Decimal") >= 0 || fullName.indexOf("Byte") >= 0) {
                return 0;
            }
        }

        return null;
    }
}
