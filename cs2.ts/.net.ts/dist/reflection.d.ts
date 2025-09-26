export type AttributeData = {
    type: string;
    ctorArgs?: any[];
    namedArgs?: Record<string, any>;
};
export type ParameterMetadata = {
    name: string;
    type: string;
    hasDefault?: boolean;
    defaultValue?: any;
    attributes?: AttributeData[];
};
export type MethodMetadata = {
    name: string;
    isPublic?: boolean;
    isStatic?: boolean;
    isAbstract?: boolean;
    isVirtual?: boolean;
    returnType?: string;
    parameters?: ParameterMetadata[];
    attributes?: AttributeData[];
    signature?: string;
};
export type PropertyMetadata = {
    name: string;
    type: string;
    isPublic?: boolean;
    isStatic?: boolean;
    canRead?: boolean;
    canWrite?: boolean;
    attributes?: AttributeData[];
};
export type FieldMetadata = {
    name: string;
    type: string;
    isPublic?: boolean;
    isStatic?: boolean;
    isInitOnly?: boolean;
    attributes?: AttributeData[];
};
export type EnumValueMetadata = {
    name: string;
    value: number | string;
};
export type TypeMetadata = {
    name: string;
    namespace?: string;
    fullName: string;
    assembly?: string;
    typeId?: string;
    isClass?: boolean;
    isInterface?: boolean;
    isStruct?: boolean;
    isEnum?: boolean;
    isArray?: boolean;
    isGeneric?: boolean;
    genericArity?: number;
    baseType?: string;
    interfaces?: string[];
    attributes?: AttributeData[];
    fields?: FieldMetadata[];
    properties?: PropertyMetadata[];
    methods?: MethodMetadata[];
    constructors?: MethodMetadata[];
    enumValues?: EnumValueMetadata[];
    elementType?: string;
};
export declare enum BindingFlags {
    Default = 0,
    IgnoreCase = 1,
    DeclaredOnly = 2,
    Instance = 4,
    Static = 8,
    Public = 16,
    NonPublic = 32,
    FlattenHierarchy = 64
}
declare const kTypeTag: unique symbol;
declare const kMemberTag: unique symbol;
export declare abstract class MemberInfo {
    readonly [kMemberTag] = true;
    readonly name: string;
    readonly declaringType: Type;
    readonly isStatic: boolean;
    readonly isPublic: boolean;
    protected readonly attributes?: AttributeData[];
    constructor(declaringType: Type, name: string, isStatic: boolean, isPublic: boolean, attributes?: AttributeData[]);
    GetCustomAttributes(): any[];
}
export declare class ParameterInfo {
    readonly name: string;
    readonly parameterType: Type;
    readonly hasDefaultValue: boolean;
    readonly defaultValue: any;
    constructor(meta: ParameterMetadata);
}
export declare class MethodInfo extends MemberInfo {
    readonly returnType: Type;
    readonly isAbstract: boolean;
    readonly isVirtual: boolean;
    readonly parameters: ParameterInfo[];
    readonly signature?: string;
    constructor(declaringType: Type, meta: MethodMetadata);
    Invoke(target: any, parameters?: any[]): any;
}
export declare class PropertyInfo extends MemberInfo {
    readonly propertyType: Type;
    readonly canRead: boolean;
    readonly canWrite: boolean;
    constructor(declaringType: Type, meta: PropertyMetadata);
    GetValue(target: any): any;
    SetValue(target: any, value: any): void;
}
export declare class FieldInfo extends MemberInfo {
    readonly fieldType: Type;
    constructor(declaringType: Type, meta: FieldMetadata);
    GetValue(target: any): any;
    SetValue(target: any, value: any): void;
}
export declare class ConstructorInfo extends MemberInfo {
    readonly parameters: ParameterInfo[];
    constructor(declaringType: Type, meta: MethodMetadata);
    Invoke(parameters?: any[]): any;
}
export declare class Type {
    readonly [kTypeTag] = true;
    private meta;
    _ctor: any | null;
    private methodsCache?;
    private propsCache?;
    private fieldsCache?;
    private ctorsCache?;
    private baseTypeResolved?;
    private interfacesResolved?;
    private static primitivesInitialized;
    private static primitiveVoid;
    private static primitiveObject;
    private static primitiveString;
    private static primitiveBoolean;
    private static primitiveNumber;
    constructor(metadata: TypeMetadata, ctor: any | null);
    _bindCtor(ctor: any): void;
    private static ensurePrimitives;
    static get Void(): Type;
    static get object(): Type;
    static get string(): Type;
    static get boolean(): Type;
    static get number(): Type;
    static get(fullNameOrCtor: string | any | null | undefined): Type | undefined;
    static GetType(fullName: string): Type | null;
    static of(obj: any): Type | null;
    get Name(): string;
    get Namespace(): string | undefined;
    get FullName(): string;
    get fullName(): string;
    get Assembly(): string;
    get IsClass(): boolean;
    get IsInterface(): boolean;
    get IsEnum(): boolean;
    get IsArray(): boolean;
    get IsGenericType(): boolean;
    get BaseType(): Type | null;
    GetInterfaces(): Type[];
    GetElementType(): Type | null;
    GetMethods(bindingFlags?: BindingFlags): MethodInfo[];
    GetMethod(name: string, bindingFlags?: BindingFlags): MethodInfo | null;
    GetProperties(bindingFlags?: BindingFlags): PropertyInfo[];
    GetProperty(name: string, bindingFlags?: BindingFlags): PropertyInfo | null;
    GetFields(bindingFlags?: BindingFlags): FieldInfo[];
    GetField(name: string, bindingFlags?: BindingFlags): FieldInfo | null;
    GetConstructors(bindingFlags?: BindingFlags): ConstructorInfo[];
    GetCustomAttributes(): any[];
    IsSubclassOf(t: Type | null): boolean;
    IsAssignableFrom(t: Type | null): boolean;
    private _ensureMethods;
    private _ensureProps;
    private _ensureFields;
    private _ensureCtors;
}
export declare function registerType(ctor: any, metadata: TypeMetadata): Type;
export declare function registerMetadata(metadata: TypeMetadata): Type;
export declare function getTypeOf(obj: any): Type | null;
export declare function getType(fullName: string): Type | null;
export declare function createInstance(type: Type, args?: any[]): any;
export declare function isType(x: any): x is Type;
export declare function isMember(x: any): x is MemberInfo;
export declare function constructAttributes(attrs?: AttributeData[]): any[];
export declare function registerEnum(ctor: any, metadata: TypeMetadata): Type;
export declare const Activator: {
    readonly CreateInstance: typeof createInstance;
};
export declare const System: {
    readonly Type: typeof Type;
    readonly BindingFlags: typeof BindingFlags;
    readonly Activator: {
        readonly CreateInstance: typeof createInstance;
    };
    readonly Reflection: {
        readonly registerType: typeof registerType;
        readonly registerEnum: typeof registerEnum;
        readonly registerMetadata: typeof registerMetadata;
        readonly getTypeOf: typeof getTypeOf;
        readonly getType: typeof getType;
        readonly BindingFlags: typeof BindingFlags;
    };
};
export default System;
