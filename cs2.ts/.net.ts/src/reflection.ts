// @ts-nocheck
/*
 Reflection runtime for cs2.ts
 - Provides a C#-like reflection API over TypeScript classes/functions
 - Consumes generator-produced metadata via registerType/registerEnum
 - Supports: Type lookup, members enumeration, attributes, and invocation
 - Minimal surface matching C#: Type/MemberInfo/MethodInfo/PropertyInfo/FieldInfo/ConstructorInfo/ParameterInfo
*/

/* eslint-disable @typescript-eslint/no-explicit-any */

export type AttributeData = {
  type: string; // full name
  ctorArgs?: any[];
  namedArgs?: Record<string, any>;
};

export type ParameterMetadata = {
  name: string;
  type: string; // full name
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
  returnType?: string; // full name
  parameters?: ParameterMetadata[];
  attributes?: AttributeData[];
  // Optional discriminator for overload grouping/signature hashing
  signature?: string;
};

export type PropertyMetadata = {
  name: string;
  type: string; // full name
  isPublic?: boolean;
  isStatic?: boolean;
  canRead?: boolean;
  canWrite?: boolean;
  attributes?: AttributeData[];
};

export type FieldMetadata = {
  name: string;
  type: string; // full name
  isPublic?: boolean;
  isStatic?: boolean;
  isInitOnly?: boolean;
  attributes?: AttributeData[];
};

export type EnumValueMetadata = { name: string; value: number | string };

export type TypeMetadata = {
  name: string;
  namespace?: string;
  fullName: string; // namespace + name
  assembly?: string;
  typeId?: string; // generator-unique id if available
  isClass?: boolean;
  isInterface?: boolean;
  isStruct?: boolean;
  isEnum?: boolean;
  isArray?: boolean;
  isGeneric?: boolean;
  genericArity?: number;
  baseType?: string; // full name
  interfaces?: string[];
  attributes?: AttributeData[];
  fields?: FieldMetadata[];
  properties?: PropertyMetadata[];
  methods?: MethodMetadata[];
  constructors?: MethodMetadata[];
  enumValues?: EnumValueMetadata[];
  elementType?: string; // for arrays
};

export enum BindingFlags {
  Default = 0,
  IgnoreCase = 1 << 0,
  DeclaredOnly = 1 << 1,
  Instance = 1 << 2,
  Static = 1 << 3,
  Public = 1 << 4,
  NonPublic = 1 << 5,
  FlattenHierarchy = 1 << 6,
}

const kTypeTag = Symbol.for("cs2.ts.reflection.type");
const kMemberTag = Symbol.for("cs2.ts.reflection.member");

class ReflectionRegistry {
  private byFullName = new Map<string, Type>();
  private byCtor = new WeakMap<any, Type>();
  private byId = new Map<string, Type>();

  register(ctor: any, metadata: TypeMetadata): Type {
    const existing = this.byFullName.get(metadata.fullName);
    if (existing) {
      if (!this.byCtor.has(ctor)) {
        this.byCtor.set(ctor, existing);
        (existing as any)._bindCtor(ctor);
      }
      return existing;
    }
    const t = new Type(metadata, ctor);
    this.byFullName.set(t.fullName, t);
    if (metadata.typeId) this.byId.set(metadata.typeId, t);
    if (ctor) this.byCtor.set(ctor, t);
    return t;
  }

  registerMetadataOnly(metadata: TypeMetadata): Type {
    const existing = this.byFullName.get(metadata.fullName);
    if (existing) return existing;
    const t = new Type(metadata, null);
    this.byFullName.set(t.fullName, t);
    if (metadata.typeId) this.byId.set(metadata.typeId, t);
    return t;
  }

  getByCtor(ctor: any | null | undefined): Type | undefined {
    if (!ctor) return undefined;
    return this.byCtor.get(ctor);
  }

  getByFullName(fullName: string): Type | undefined {
    return this.byFullName.get(fullName);
  }

  getById(id: string): Type | undefined {
    return this.byId.get(id);
  }
}

const registry = new ReflectionRegistry();

export abstract class MemberInfo {
  readonly [kMemberTag] = true;
  readonly name: string;
  readonly declaringType: Type;
  readonly isStatic: boolean;
  readonly isPublic: boolean;
  protected readonly attributes?: AttributeData[];

  constructor(declaringType: Type, name: string, isStatic: boolean, isPublic: boolean, attributes?: AttributeData[]) {
    this.declaringType = declaringType;
    this.name = name;
    this.isStatic = !!isStatic;
    this.isPublic = !!isPublic;
    this.attributes = attributes;
  }

  GetCustomAttributes(): any[] {
    return (this.attributes ?? []).slice();
  }
}

export class ParameterInfo {
  readonly name: string;
  readonly parameterType: Type;
  readonly hasDefaultValue: boolean;
  readonly defaultValue: any;

  constructor(meta: ParameterMetadata) {
    this.name = meta.name;
    this.parameterType = Type.get(meta.type) ?? Type.object;
    this.hasDefaultValue = !!meta.hasDefault;
    this.defaultValue = meta.defaultValue;
  }
}

export class MethodInfo extends MemberInfo {
  readonly returnType: Type;
  readonly isAbstract: boolean;
  readonly isVirtual: boolean;
  readonly parameters: ParameterInfo[];
  readonly signature?: string;

  constructor(declaringType: Type, meta: MethodMetadata) {
    super(declaringType, meta.name, !!meta.isStatic, !!meta.isPublic, meta.attributes);
    this.returnType = Type.get(meta.returnType ?? "System.Void") ?? Type.void;
    this.isAbstract = !!meta.isAbstract;
    this.isVirtual = !!meta.isVirtual;
    this.parameters = (meta.parameters ?? []).map(p => new ParameterInfo(p));
    this.signature = meta.signature;
  }

  Invoke(target: any, parameters: any[] = []): any {
    const owner = this.isStatic ? this.declaringType._ctor : target;
    if (!owner) throw new Error(`Method invoke failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
    const fn = (owner as any)[this.name];
    if (typeof fn !== "function") {
      throw new Error(`Method not found at runtime: ${this.declaringType.fullName}.${this.isStatic ? "(static)." : ""}${this.name}`);
    }
    return fn.apply(this.isStatic ? owner : target, parameters);
  }
}

export class PropertyInfo extends MemberInfo {
  readonly propertyType: Type;
  readonly canRead: boolean;
  readonly canWrite: boolean;

  constructor(declaringType: Type, meta: PropertyMetadata) {
    super(declaringType, meta.name, !!meta.isStatic, !!meta.isPublic, meta.attributes);
    this.propertyType = Type.get(meta.type) ?? Type.object;
    this.canRead = meta.canRead ?? true;
    this.canWrite = meta.canWrite ?? true;
  }

  GetValue(target: any): any {
    const owner = this.isStatic ? this.declaringType._ctor : target;
    if (!owner) throw new Error(`Property get failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
    return (owner as any)[this.name];
  }

  SetValue(target: any, value: any): void {
    const owner = this.isStatic ? this.declaringType._ctor : target;
    if (!owner) throw new Error(`Property set failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
    (owner as any)[this.name] = value;
  }
}

export class FieldInfo extends MemberInfo {
  readonly fieldType: Type;

  constructor(declaringType: Type, meta: FieldMetadata) {
    super(declaringType, meta.name, !!meta.isStatic, !!meta.isPublic, meta.attributes);
    this.fieldType = Type.get(meta.type) ?? Type.object;
  }

  GetValue(target: any): any {
    const owner = this.isStatic ? this.declaringType._ctor : target;
    if (!owner) throw new Error(`Field get failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
    return (owner as any)[this.name];
  }

  SetValue(target: any, value: any): void {
    const owner = this.isStatic ? this.declaringType._ctor : target;
    if (!owner) throw new Error(`Field set failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
    (owner as any)[this.name] = value;
  }
}

export class ConstructorInfo extends MemberInfo {
  readonly parameters: ParameterInfo[];

  constructor(declaringType: Type, meta: MethodMetadata) {
    super(declaringType, ".ctor", false, !!meta.isPublic, meta.attributes);
    this.parameters = (meta.parameters ?? []).map(p => new ParameterInfo(p));
  }

  Invoke(parameters: any[] = []): any {
    const ctor = this.declaringType._ctor;
    if (!ctor) throw new Error(`Constructor invoke failed: missing ctor for ${this.declaringType.fullName}`);
    switch (parameters.length) {
      case 0: return new (ctor as any)();
      case 1: return new (ctor as any)(parameters[0]);
      case 2: return new (ctor as any)(parameters[0], parameters[1]);
      case 3: return new (ctor as any)(parameters[0], parameters[1], parameters[2]);
      default:
        return Reflect.construct(ctor, parameters);
    }
  }
}

export class Type {
  readonly [kTypeTag] = true;
  private meta: TypeMetadata;
  _ctor: any | null;

  private methodsCache?: MethodInfo[];
  private propsCache?: PropertyInfo[];
  private fieldsCache?: FieldInfo[];
  private ctorsCache?: ConstructorInfo[];
  private baseTypeResolved?: Type | null;
  private interfacesResolved?: Type[];

  static readonly void = registry.registerMetadataOnly({ name: "Void", fullName: "System.Void", isClass: false });
  static readonly object = registry.registerMetadataOnly({ name: "Object", fullName: "System.Object", isClass: true });
  static readonly string = registry.registerMetadataOnly({ name: "String", fullName: "System.String", isClass: true });
  static readonly boolean = registry.registerMetadataOnly({ name: "Boolean", fullName: "System.Boolean", isClass: true });
  static readonly number = registry.registerMetadataOnly({ name: "Double", fullName: "System.Double", isClass: true });

  constructor(metadata: TypeMetadata, ctor: any | null) {
    this.meta = metadata;
    this._ctor = ctor;
  }

  _bindCtor(ctor: any) { this._ctor = ctor; }

  static get(fullNameOrCtor: string | any | null | undefined): Type | undefined {
    if (!fullNameOrCtor) return undefined;
    if (typeof fullNameOrCtor === "string") return registry.getByFullName(fullNameOrCtor);
    return registry.getByCtor(fullNameOrCtor);
  }

  static GetType(fullName: string): Type | null { return registry.getByFullName(fullName) ?? null; }

  static of(obj: any): Type | null {
    if (obj == null) return null;
    const ctor = obj.constructor;
    return (registry.getByCtor(ctor) ?? Type.object) as Type;
  }

  get Name(): string { return this.meta.name; }
  get Namespace(): string | undefined { return this.meta.namespace; }
  get FullName(): string { return this.meta.fullName; }
  get fullName(): string { return this.meta.fullName; }
  get Assembly(): string { return this.meta.assembly ?? "Generated"; }
  get IsClass(): boolean { return !!this.meta.isClass || (!this.meta.isInterface && !this.meta.isEnum && !this.meta.isArray); }
  get IsInterface(): boolean { return !!this.meta.isInterface; }
  get IsEnum(): boolean { return !!this.meta.isEnum; }
  get IsArray(): boolean { return !!this.meta.isArray; }
  get IsGenericType(): boolean { return !!this.meta.isGeneric; }
  get BaseType(): Type | null {
    if (this.baseTypeResolved !== undefined) return this.baseTypeResolved;
    this.baseTypeResolved = this.meta.baseType ? (Type.get(this.meta.baseType) ?? null) : null;
    return this.baseTypeResolved;
  }
  GetInterfaces(): Type[] {
    if (this.interfacesResolved) return this.interfacesResolved;
    this.interfacesResolved = (this.meta.interfaces ?? []).map(n => Type.get(n) ?? Type.object);
    return this.interfacesResolved;
  }
  GetElementType(): Type | null {
    if (!this.meta.elementType) return null;
    return Type.get(this.meta.elementType) ?? null;
  }

  GetMethods(bindingFlags: BindingFlags = BindingFlags.Public | BindingFlags.Instance): MethodInfo[] {
    const all = this._ensureMethods();
    return filterMembers(all, bindingFlags, this);
  }
  GetMethod(name: string, bindingFlags: BindingFlags = BindingFlags.Public | BindingFlags.Instance): MethodInfo | null {
    const methods = this.GetMethods(bindingFlags);
    const cmp = (a: string, b: string) => (bindingFlags & BindingFlags.IgnoreCase) ? a.toLowerCase() === b.toLowerCase() : a === b;
    return methods.find(m => cmp(m.name, name)) ?? null;
  }

  GetProperties(bindingFlags: BindingFlags = BindingFlags.Public | BindingFlags.Instance): PropertyInfo[] {
    const all = this._ensureProps();
    return filterMembers(all, bindingFlags, this);
  }
  GetProperty(name: string, bindingFlags: BindingFlags = BindingFlags.Public | BindingFlags.Instance): PropertyInfo | null {
    const props = this.GetProperties(bindingFlags);
    const cmp = (a: string, b: string) => (bindingFlags & BindingFlags.IgnoreCase) ? a.toLowerCase() === b.toLowerCase() : a === b;
    return props.find(p => cmp(p.name, name)) ?? null;
  }

  GetFields(bindingFlags: BindingFlags = BindingFlags.Public | BindingFlags.Instance): FieldInfo[] {
    const all = this._ensureFields();
    return filterMembers(all, bindingFlags, this);
  }
  GetField(name: string, bindingFlags: BindingFlags = BindingFlags.Public | BindingFlags.Instance): FieldInfo | null {
    const fields = this.GetFields(bindingFlags);
    const cmp = (a: string, b: string) => (bindingFlags & BindingFlags.IgnoreCase) ? a.toLowerCase() === b.toLowerCase() : a === b;
    return fields.find(f => cmp(f.name, name)) ?? null;
  }

  GetConstructors(bindingFlags: BindingFlags = BindingFlags.Public | BindingFlags.Instance): ConstructorInfo[] {
    const all = this._ensureCtors();
    const isPublic = (c: ConstructorInfo) => c.isPublic || (!!(bindingFlags & BindingFlags.NonPublic));
    let items = all.filter(isPublic);
    if (bindingFlags & BindingFlags.DeclaredOnly) items = items.filter(c => c.declaringType === this);
    return items;
  }

  GetCustomAttributes(): any[] { return (this.meta.attributes ?? []).slice(); }

  IsSubclassOf(t: Type | null): boolean {
    if (!t) return false;
    let b: Type | null = this.BaseType;
    while (b) { if (b === t) return true; b = b.BaseType; }
    return false;
  }

  IsAssignableFrom(t: Type | null): boolean {
    if (!t) return false;
    if (this === t) return true;
    if (t.IsSubclassOf(this)) return true;
    if (this.IsInterface) return t.GetInterfaces().some(i => i === this);
    return false;
  }

  private _ensureMethods(): MethodInfo[] {
    if (!this.methodsCache) {
      const declared = (this.meta.methods ?? []).map(m => new MethodInfo(this, m));
      if (this.meta.baseType && !(this.meta.isInterface)) {
        const base = this.BaseType;
        const inherited = base ? base._ensureMethods() : [];
        this.methodsCache = [...declared, ...inherited];
      } else {
        this.methodsCache = declared;
      }
    }
    return this.methodsCache;
    }

  private _ensureProps(): PropertyInfo[] {
    if (!this.propsCache) {
      const declared = (this.meta.properties ?? []).map(p => new PropertyInfo(this, p));
      if (this.meta.baseType && !(this.meta.isInterface)) {
        const base = this.BaseType;
        const inherited = base ? base._ensureProps() : [];
        this.propsCache = [...declared, ...inherited];
      } else {
        this.propsCache = declared;
      }
    }
    return this.propsCache;
  }

  private _ensureFields(): FieldInfo[] {
    if (!this.fieldsCache) {
      const declared = (this.meta.fields ?? []).map(f => new FieldInfo(this, f));
      if (this.meta.baseType && !(this.meta.isInterface)) {
        const base = this.BaseType;
        const inherited = base ? base._ensureFields() : [];
        this.fieldsCache = [...declared, ...inherited];
      } else {
        this.fieldsCache = declared;
      }
    }
    return this.fieldsCache;
  }

  private _ensureCtors(): ConstructorInfo[] {
    if (!this.ctorsCache) {
      this.ctorsCache = (this.meta.constructors ?? [{ isPublic: true, name: ".ctor", parameters: [] }]).map(c => new ConstructorInfo(this, c));
    }
    return this.ctorsCache;
  }
}

function filterMembers<T extends MemberInfo>(items: T[], bindingFlags: BindingFlags, self: Type): T[] {
  const isPublic = (m: MemberInfo) => m.isPublic || (!!(bindingFlags & BindingFlags.NonPublic));
  const isStaticAllowed = (m: MemberInfo) => (bindingFlags & BindingFlags.Static) ? m.isStatic : true;
  const isInstanceAllowed = (m: MemberInfo) => (bindingFlags & BindingFlags.Instance) ? !m.isStatic : true;
  const declaredOnly = (m: MemberInfo) => (bindingFlags & BindingFlags.DeclaredOnly) ? m.declaringType === self : true;
  const staticHierarchy = (m: MemberInfo) => {
    if (!m.isStatic) return true;
    if ((bindingFlags & BindingFlags.Static) && !(bindingFlags & BindingFlags.FlattenHierarchy)) {
      return m.declaringType === self;
    }
    return true;
  };
  return items.filter(m => isPublic(m) && isStaticAllowed(m) && isInstanceAllowed(m) && declaredOnly(m) && staticHierarchy(m));
}

export function registerType(ctor: any, metadata: TypeMetadata): Type {
  return registry.register(ctor, metadata);
}

export function registerMetadata(metadata: TypeMetadata): Type {
  return registry.registerMetadataOnly(metadata);
}

export function getTypeOf(obj: any): Type | null { return Type.of(obj); }

export function getType(fullName: string): Type | null { return Type.GetType(fullName); }

export function createInstance(type: Type, args: any[] = []): any {
  const ci = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
  if (!ci) throw new Error(`No constructor found for ${type.fullName}`);
  return ci.Invoke(args);
}

export function isType(x: any): x is Type { return !!x && (x as any)[kTypeTag] === true; }

export function isMember(x: any): x is MemberInfo { return !!x && (x as any)[kMemberTag] === true; }

export function constructAttributes(attrs?: AttributeData[]): any[] {
  return (attrs ?? []).map(a => ({ ...a }));
}

export function registerEnum(ctor: any, metadata: TypeMetadata): Type {
  if (!metadata.isEnum) metadata.isEnum = true;
  if (!metadata.enumValues) {
    const values: EnumValueMetadata[] = [];
    for (const k of Object.keys(ctor)) {
      const v = (ctor as any)[k];
      if (typeof v === "number" || typeof v === "string") values.push({ name: k, value: v });
    }
    metadata.enumValues = values;
  }
  return registry.register(ctor, metadata);
}

export const Activator = { CreateInstance: createInstance } as const;

export const System = {
  Type,
  BindingFlags,
  Activator,
  Reflection: { registerType, registerEnum, registerMetadata, getTypeOf, getType, BindingFlags },
} as const;

export default System;

