// @ts-nocheck
import { IReadOnlyCollection } from "./ireadonlycollection";

export interface IReadOnlyList<T> extends IReadOnlyCollection<T> {
    readonly length: number;
    readonly count: number;
    readonly [index: number]: T;
}
