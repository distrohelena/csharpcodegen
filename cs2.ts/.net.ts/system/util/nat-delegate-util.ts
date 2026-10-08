type NativeDelegate = (...args: any[]) => any;

/** Preserves CLR multicast delegate combination and removal for generated callback fields. */
export class NativeDelegateUtil {
    private static readonly invocationLists = new WeakMap<NativeDelegate, NativeDelegate[]>();

    /** Combines two delegates in invocation order. */
    public static combine<T extends NativeDelegate>(source: T | null | undefined, value: T | null | undefined): T | null {
        if (!source) {
            return value ?? null;
        }
        if (!value) {
            return source;
        }

        return this.build([...this.list(source), ...this.list(value)]) as T;
    }

    /** Removes the last matching invocation list from a delegate. */
    public static remove<T extends NativeDelegate>(source: T | null | undefined, value: T | null | undefined): T | null {
        if (!source || !value) {
            return source ?? null;
        }

        const sourceList = [...this.list(source)];
        const valueList = this.list(value);
        let match = -1;
        for (let start = sourceList.length - valueList.length; start >= 0; start--) {
            if (valueList.every((handler, index) => sourceList[start + index] === handler)) {
                match = start;
                break;
            }
        }
        if (match < 0) {
            return source;
        }

        sourceList.splice(match, valueList.length);
        if (sourceList.length === 0) {
            return null;
        }
        if (sourceList.length === 1) {
            return sourceList[0] as T;
        }
        return this.build(sourceList) as T;
    }

    private static list(value: NativeDelegate): NativeDelegate[] {
        return this.invocationLists.get(value) ?? [value];
    }

    private static build(invocationList: NativeDelegate[]): NativeDelegate {
        const combined = function (this: any, ...args: any[]): any {
            let result: any;
            for (const handler of invocationList) {
                result = handler.apply(this, args);
            }
            return result;
        };
        this.invocationLists.set(combined, invocationList);
        return combined;
    }
}
