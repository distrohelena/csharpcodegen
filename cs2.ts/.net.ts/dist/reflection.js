"use strict";
// @ts-nocheck
/*
 Reflection runtime for cs2.ts
 - Provides a C#-like reflection API over TypeScript classes/functions
 - Consumes generator-produced metadata via registerType/registerEnum
 - Supports: Type lookup, members enumeration, attributes, and invocation
 - Minimal surface matching C#: Type/MemberInfo/MethodInfo/PropertyInfo/FieldInfo/ConstructorInfo/ParameterInfo
*/
var _a, _b;
Object.defineProperty(exports, "__esModule", { value: true });
exports.System = exports.Activator = exports.Type = exports.ConstructorInfo = exports.FieldInfo = exports.PropertyInfo = exports.MethodInfo = exports.ParameterInfo = exports.MemberInfo = exports.BindingFlags = void 0;
exports.registerType = registerType;
exports.registerMetadata = registerMetadata;
exports.getTypeOf = getTypeOf;
exports.getType = getType;
exports.createInstance = createInstance;
exports.isType = isType;
exports.isMember = isMember;
exports.constructAttributes = constructAttributes;
exports.registerEnum = registerEnum;
var BindingFlags;
(function (BindingFlags) {
    BindingFlags[BindingFlags["Default"] = 0] = "Default";
    BindingFlags[BindingFlags["IgnoreCase"] = 1] = "IgnoreCase";
    BindingFlags[BindingFlags["DeclaredOnly"] = 2] = "DeclaredOnly";
    BindingFlags[BindingFlags["Instance"] = 4] = "Instance";
    BindingFlags[BindingFlags["Static"] = 8] = "Static";
    BindingFlags[BindingFlags["Public"] = 16] = "Public";
    BindingFlags[BindingFlags["NonPublic"] = 32] = "NonPublic";
    BindingFlags[BindingFlags["FlattenHierarchy"] = 64] = "FlattenHierarchy";
})(BindingFlags || (exports.BindingFlags = BindingFlags = {}));
const kTypeTag = Symbol.for("cs2.ts.reflection.type");
const kMemberTag = Symbol.for("cs2.ts.reflection.member");
class ReflectionRegistry {
    constructor() {
        this.byFullName = new Map();
        this.byCtor = new WeakMap();
        this.byId = new Map();
    }
    register(ctor, metadata) {
        var _c;
        const existing = this.byFullName.get(metadata.fullName);
        if (existing) {
            if (ctor && !this.byCtor.has(ctor)) {
                const type = existing.toType();
                (_c = type._bindCtor) === null || _c === void 0 ? void 0 : _c.call(type, ctor);
                this.byCtor.set(ctor, existing);
            }
            return existing.toType();
        }
        const typeHolder = new LazyType(metadata, ctor);
        this.byFullName.set(metadata.fullName, typeHolder);
        if (metadata.typeId)
            this.byId.set(metadata.typeId, typeHolder);
        if (ctor)
            this.byCtor.set(ctor, typeHolder);
        return typeHolder.toType();
    }
    registerMetadataOnly(metadata) {
        const existing = this.byFullName.get(metadata.fullName);
        if (existing)
            return existing.toType();
        const typeHolder = new LazyType(metadata, null);
        this.byFullName.set(metadata.fullName, typeHolder);
        if (metadata.typeId)
            this.byId.set(metadata.typeId, typeHolder);
        return typeHolder.toType();
    }
    getByCtor(ctor) {
        if (!ctor)
            return undefined;
        const handle = this.byCtor.get(ctor);
        return handle === null || handle === void 0 ? void 0 : handle.toType();
    }
    getByFullName(fullName) {
        const handle = this.byFullName.get(fullName);
        return handle === null || handle === void 0 ? void 0 : handle.toType();
    }
    getById(id) {
        const handle = this.byId.get(id);
        return handle === null || handle === void 0 ? void 0 : handle.toType();
    }
}
const registry = new ReflectionRegistry();
class LazyType {
    constructor(metadata, ctor) {
        this.instance = null;
        this.meta = metadata;
        if (ctor) {
            this.instance = new Type(metadata, ctor);
        }
    }
    get fullName() {
        return this.meta.fullName;
    }
    toType() {
        if (!this.instance) {
            this.instance = new Type(this.meta, null);
        }
        return this.instance;
    }
}
class MemberInfo {
    constructor(declaringType, name, isStatic, isPublic, attributes) {
        this[_a] = true;
        this.declaringType = declaringType;
        this.name = name;
        this.isStatic = !!isStatic;
        this.isPublic = !!isPublic;
        this.attributes = attributes;
    }
    GetCustomAttributes() {
        var _c;
        return ((_c = this.attributes) !== null && _c !== void 0 ? _c : []).slice();
    }
}
exports.MemberInfo = MemberInfo;
_a = kMemberTag;
class ParameterInfo {
    constructor(meta) {
        var _c;
        this.name = meta.name;
        this.parameterType = (_c = Type.get(meta.type)) !== null && _c !== void 0 ? _c : Type.object;
        this.hasDefaultValue = !!meta.hasDefault;
        this.defaultValue = meta.defaultValue;
    }
}
exports.ParameterInfo = ParameterInfo;
class MethodInfo extends MemberInfo {
    constructor(declaringType, meta) {
        var _c, _d, _e;
        super(declaringType, meta.name, !!meta.isStatic, !!meta.isPublic, meta.attributes);
        this.returnType = (_d = Type.get((_c = meta.returnType) !== null && _c !== void 0 ? _c : "System.Void")) !== null && _d !== void 0 ? _d : Type.void;
        this.isAbstract = !!meta.isAbstract;
        this.isVirtual = !!meta.isVirtual;
        this.parameters = ((_e = meta.parameters) !== null && _e !== void 0 ? _e : []).map(p => new ParameterInfo(p));
        this.signature = meta.signature;
    }
    Invoke(target, parameters = []) {
        const owner = this.isStatic ? this.declaringType._ctor : target;
        if (!owner)
            throw new Error(`Method invoke failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
        const fn = owner[this.name];
        if (typeof fn !== "function") {
            throw new Error(`Method not found at runtime: ${this.declaringType.fullName}.${this.isStatic ? "(static)." : ""}${this.name}`);
        }
        return fn.apply(this.isStatic ? owner : target, parameters);
    }
}
exports.MethodInfo = MethodInfo;
class PropertyInfo extends MemberInfo {
    constructor(declaringType, meta) {
        var _c, _d, _e;
        super(declaringType, meta.name, !!meta.isStatic, !!meta.isPublic, meta.attributes);
        this.propertyType = (_c = Type.get(meta.type)) !== null && _c !== void 0 ? _c : Type.object;
        this.canRead = (_d = meta.canRead) !== null && _d !== void 0 ? _d : true;
        this.canWrite = (_e = meta.canWrite) !== null && _e !== void 0 ? _e : true;
    }
    GetValue(target) {
        const owner = this.isStatic ? this.declaringType._ctor : target;
        if (!owner)
            throw new Error(`Property get failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
        return owner[this.name];
    }
    SetValue(target, value) {
        const owner = this.isStatic ? this.declaringType._ctor : target;
        if (!owner)
            throw new Error(`Property set failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
        owner[this.name] = value;
    }
}
exports.PropertyInfo = PropertyInfo;
class FieldInfo extends MemberInfo {
    constructor(declaringType, meta) {
        var _c;
        super(declaringType, meta.name, !!meta.isStatic, !!meta.isPublic, meta.attributes);
        this.fieldType = (_c = Type.get(meta.type)) !== null && _c !== void 0 ? _c : Type.object;
    }
    GetValue(target) {
        const owner = this.isStatic ? this.declaringType._ctor : target;
        if (!owner)
            throw new Error(`Field get failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
        return owner[this.name];
    }
    SetValue(target, value) {
        const owner = this.isStatic ? this.declaringType._ctor : target;
        if (!owner)
            throw new Error(`Field set failed: missing owner for ${this.declaringType.fullName}.${this.name}`);
        owner[this.name] = value;
    }
}
exports.FieldInfo = FieldInfo;
class ConstructorInfo extends MemberInfo {
    constructor(declaringType, meta) {
        var _c;
        super(declaringType, ".ctor", false, !!meta.isPublic, meta.attributes);
        this.parameters = ((_c = meta.parameters) !== null && _c !== void 0 ? _c : []).map(p => new ParameterInfo(p));
    }
    Invoke(parameters = []) {
        const ctor = this.declaringType._ctor;
        if (!ctor)
            throw new Error(`Constructor invoke failed: missing ctor for ${this.declaringType.fullName}`);
        switch (parameters.length) {
            case 0: return new ctor();
            case 1: return new ctor(parameters[0]);
            case 2: return new ctor(parameters[0], parameters[1]);
            case 3: return new ctor(parameters[0], parameters[1], parameters[2]);
            default:
                return Reflect.construct(ctor, parameters);
        }
    }
}
exports.ConstructorInfo = ConstructorInfo;
class Type {
    constructor(metadata, ctor) {
        this[_b] = true;
        this.meta = metadata;
        this._ctor = ctor;
    }
    _bindCtor(ctor) { this._ctor = ctor; }
    static ensurePrimitives() {
        if (Type.primitivesInitialized) {
            return;
        }
        Type.primitiveVoid = registry.registerMetadataOnly({ name: "Void", fullName: "System.Void", isClass: false });
        Type.primitiveObject = registry.registerMetadataOnly({ name: "Object", fullName: "System.Object", isClass: true });
        Type.primitiveString = registry.registerMetadataOnly({ name: "String", fullName: "System.String", isClass: true });
        Type.primitiveBoolean = registry.registerMetadataOnly({ name: "Boolean", fullName: "System.Boolean", isClass: true });
        Type.primitiveNumber = registry.registerMetadataOnly({ name: "Double", fullName: "System.Double", isClass: true });
        Type.primitivesInitialized = true;
    }
    static get Void() {
        Type.ensurePrimitives();
        return Type.primitiveVoid;
    }
    static get object() {
        Type.ensurePrimitives();
        return Type.primitiveObject;
    }
    static get string() {
        Type.ensurePrimitives();
        return Type.primitiveString;
    }
    static get boolean() {
        Type.ensurePrimitives();
        return Type.primitiveBoolean;
    }
    static get number() {
        Type.ensurePrimitives();
        return Type.primitiveNumber;
    }
    static get(fullNameOrCtor) {
        if (!fullNameOrCtor)
            return undefined;
        if (typeof fullNameOrCtor === "string")
            return registry.getByFullName(fullNameOrCtor);
        return registry.getByCtor(fullNameOrCtor);
    }
    static GetType(fullName) { var _c; return (_c = registry.getByFullName(fullName)) !== null && _c !== void 0 ? _c : null; }
    static of(obj) {
        var _c;
        if (obj == null)
            return null;
        const ctor = obj.constructor;
        return ((_c = registry.getByCtor(ctor)) !== null && _c !== void 0 ? _c : Type.object);
    }
    get Name() { return this.meta.name; }
    get Namespace() { return this.meta.namespace; }
    get FullName() { return this.meta.fullName; }
    get fullName() { return this.meta.fullName; }
    get Assembly() { var _c; return (_c = this.meta.assembly) !== null && _c !== void 0 ? _c : "Generated"; }
    get IsClass() { return !!this.meta.isClass || (!this.meta.isInterface && !this.meta.isEnum && !this.meta.isArray); }
    get IsInterface() { return !!this.meta.isInterface; }
    get IsEnum() { return !!this.meta.isEnum; }
    get IsArray() { return !!this.meta.isArray; }
    get IsGenericType() { return !!this.meta.isGeneric; }
    get BaseType() {
        var _c;
        if (this.baseTypeResolved !== undefined)
            return this.baseTypeResolved;
        this.baseTypeResolved = this.meta.baseType ? ((_c = Type.get(this.meta.baseType)) !== null && _c !== void 0 ? _c : null) : null;
        return this.baseTypeResolved;
    }
    GetInterfaces() {
        var _c;
        if (this.interfacesResolved)
            return this.interfacesResolved;
        this.interfacesResolved = ((_c = this.meta.interfaces) !== null && _c !== void 0 ? _c : []).map(n => { var _c; return (_c = Type.get(n)) !== null && _c !== void 0 ? _c : Type.object; });
        return this.interfacesResolved;
    }
    GetElementType() {
        var _c;
        if (!this.meta.elementType)
            return null;
        return (_c = Type.get(this.meta.elementType)) !== null && _c !== void 0 ? _c : null;
    }
    GetMethods(bindingFlags = BindingFlags.Public | BindingFlags.Instance) {
        const all = this._ensureMethods();
        return filterMembers(all, bindingFlags, this);
    }
    GetMethod(name, bindingFlags = BindingFlags.Public | BindingFlags.Instance) {
        var _c;
        const methods = this.GetMethods(bindingFlags);
        const cmp = (a, b) => (bindingFlags & BindingFlags.IgnoreCase) ? a.toLowerCase() === b.toLowerCase() : a === b;
        return (_c = methods.find(m => cmp(m.name, name))) !== null && _c !== void 0 ? _c : null;
    }
    GetProperties(bindingFlags = BindingFlags.Public | BindingFlags.Instance) {
        const all = this._ensureProps();
        return filterMembers(all, bindingFlags, this);
    }
    GetProperty(name, bindingFlags = BindingFlags.Public | BindingFlags.Instance) {
        var _c;
        const props = this.GetProperties(bindingFlags);
        const cmp = (a, b) => (bindingFlags & BindingFlags.IgnoreCase) ? a.toLowerCase() === b.toLowerCase() : a === b;
        return (_c = props.find(p => cmp(p.name, name))) !== null && _c !== void 0 ? _c : null;
    }
    GetFields(bindingFlags = BindingFlags.Public | BindingFlags.Instance) {
        const all = this._ensureFields();
        return filterMembers(all, bindingFlags, this);
    }
    GetField(name, bindingFlags = BindingFlags.Public | BindingFlags.Instance) {
        var _c;
        const fields = this.GetFields(bindingFlags);
        const cmp = (a, b) => (bindingFlags & BindingFlags.IgnoreCase) ? a.toLowerCase() === b.toLowerCase() : a === b;
        return (_c = fields.find(f => cmp(f.name, name))) !== null && _c !== void 0 ? _c : null;
    }
    GetConstructors(bindingFlags = BindingFlags.Public | BindingFlags.Instance) {
        const all = this._ensureCtors();
        const isPublic = (c) => c.isPublic || (!!(bindingFlags & BindingFlags.NonPublic));
        let items = all.filter(isPublic);
        if (bindingFlags & BindingFlags.DeclaredOnly)
            items = items.filter(c => c.declaringType === this);
        return items;
    }
    GetCustomAttributes() { var _c; return ((_c = this.meta.attributes) !== null && _c !== void 0 ? _c : []).slice(); }
    IsSubclassOf(t) {
        if (!t)
            return false;
        let b = this.BaseType;
        while (b) {
            if (b === t)
                return true;
            b = b.BaseType;
        }
        return false;
    }
    IsAssignableFrom(t) {
        if (!t)
            return false;
        if (this === t)
            return true;
        if (t.IsSubclassOf(this))
            return true;
        if (this.IsInterface)
            return t.GetInterfaces().some(i => i === this);
        return false;
    }
    _ensureMethods() {
        var _c;
        if (!this.methodsCache) {
            const declared = ((_c = this.meta.methods) !== null && _c !== void 0 ? _c : []).map(m => new MethodInfo(this, m));
            if (this.meta.baseType && !(this.meta.isInterface)) {
                const base = this.BaseType;
                const inherited = base ? base._ensureMethods() : [];
                this.methodsCache = [...declared, ...inherited];
            }
            else {
                this.methodsCache = declared;
            }
        }
        return this.methodsCache;
    }
    _ensureProps() {
        var _c;
        if (!this.propsCache) {
            const declared = ((_c = this.meta.properties) !== null && _c !== void 0 ? _c : []).map(p => new PropertyInfo(this, p));
            if (this.meta.baseType && !(this.meta.isInterface)) {
                const base = this.BaseType;
                const inherited = base ? base._ensureProps() : [];
                this.propsCache = [...declared, ...inherited];
            }
            else {
                this.propsCache = declared;
            }
        }
        return this.propsCache;
    }
    _ensureFields() {
        var _c;
        if (!this.fieldsCache) {
            const declared = ((_c = this.meta.fields) !== null && _c !== void 0 ? _c : []).map(f => new FieldInfo(this, f));
            if (this.meta.baseType && !(this.meta.isInterface)) {
                const base = this.BaseType;
                const inherited = base ? base._ensureFields() : [];
                this.fieldsCache = [...declared, ...inherited];
            }
            else {
                this.fieldsCache = declared;
            }
        }
        return this.fieldsCache;
    }
    _ensureCtors() {
        var _c;
        if (!this.ctorsCache) {
            this.ctorsCache = ((_c = this.meta.constructors) !== null && _c !== void 0 ? _c : [{ isPublic: true, name: ".ctor", parameters: [] }]).map(c => new ConstructorInfo(this, c));
        }
        return this.ctorsCache;
    }
}
exports.Type = Type;
_b = kTypeTag;
Type.primitivesInitialized = false;
function filterMembers(items, bindingFlags, self) {
    const isPublic = (m) => m.isPublic || (!!(bindingFlags & BindingFlags.NonPublic));
    const isStaticAllowed = (m) => (bindingFlags & BindingFlags.Static) ? m.isStatic : true;
    const isInstanceAllowed = (m) => (bindingFlags & BindingFlags.Instance) ? !m.isStatic : true;
    const declaredOnly = (m) => (bindingFlags & BindingFlags.DeclaredOnly) ? m.declaringType === self : true;
    const staticHierarchy = (m) => {
        if (!m.isStatic)
            return true;
        if ((bindingFlags & BindingFlags.Static) && !(bindingFlags & BindingFlags.FlattenHierarchy)) {
            return m.declaringType === self;
        }
        return true;
    };
    return items.filter(m => isPublic(m) && isStaticAllowed(m) && isInstanceAllowed(m) && declaredOnly(m) && staticHierarchy(m));
}
function registerType(ctor, metadata) {
    return registry.register(ctor, metadata);
}
function registerMetadata(metadata) {
    return registry.registerMetadataOnly(metadata);
}
function getTypeOf(obj) { return Type.of(obj); }
function getType(fullName) { return Type.GetType(fullName); }
function createInstance(type, args = []) {
    const ci = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
    if (!ci)
        throw new Error(`No constructor found for ${type.fullName}`);
    return ci.Invoke(args);
}
function isType(x) { return !!x && x[kTypeTag] === true; }
function isMember(x) { return !!x && x[kMemberTag] === true; }
function constructAttributes(attrs) {
    return (attrs !== null && attrs !== void 0 ? attrs : []).map(a => ({ ...a }));
}
function registerEnum(ctor, metadata) {
    if (!metadata.isEnum)
        metadata.isEnum = true;
    if (!metadata.enumValues) {
        const values = [];
        for (const k of Object.keys(ctor)) {
            const v = ctor[k];
            if (typeof v === "number" || typeof v === "string")
                values.push({ name: k, value: v });
        }
        metadata.enumValues = values;
    }
    return registry.register(ctor, metadata);
}
exports.Activator = { CreateInstance: createInstance };
exports.System = {
    Type,
    BindingFlags,
    Activator: exports.Activator,
    Reflection: { registerType, registerEnum, registerMetadata, getTypeOf, getType, BindingFlags },
};
exports.default = exports.System;
