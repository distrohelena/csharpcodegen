cs2.ts .NET runtime helpers

This folder provides the minimal runtime required for C# features when targeting TypeScript. The reflection layer is compatible with C#-style APIs so that code using Reflection can be translated and executed in TypeScript.

Key entry points
- `registerType(ctor, metadata)`: Attach generator-produced metadata to a class/ctor. Returns a `Type` instance.
- `registerEnum(ctor, metadata)`: Register an enum type and map values.
- `Type.GetType(fullName)` / `getType(fullName)`: Lookup a type by full name.
- `getTypeOf(obj)` / `obj.GetType()` translation: Return runtime type of an object.
- `BindingFlags`: Bit flags compatible with C# filtering.
- `Type` methods: `GetMethods`, `GetProperties`, `GetFields`, `GetConstructors`, `GetCustomAttributes`, `IsSubclassOf`, `IsAssignableFrom`.
- `MethodInfo.Invoke`, `PropertyInfo.GetValue/SetValue`, `FieldInfo.GetValue/SetValue`, `ConstructorInfo.Invoke`.
- `Activator.CreateInstance(type, args?)`: Instantiate via constructor.

Metadata shape (generator contract)
```
TypeMetadata = {
  name: string,
  namespace?: string,
  fullName: string,              // e.g., "My.App.Foo"
  typeId?: string,
  isClass?: boolean,
  isInterface?: boolean,
  isEnum?: boolean,
  isArray?: boolean,
  isGeneric?: boolean,
  genericArity?: number,
  baseType?: string,             // full name
  interfaces?: string[],
  attributes?: AttributeData[],
  fields?: FieldMetadata[],
  properties?: PropertyMetadata[],
  methods?: MethodMetadata[],    // instance + static, flagged via isStatic
  constructors?: MethodMetadata[],
  enumValues?: { name, value }[],
  elementType?: string,          // for arrays
}
```

Integration with generator
- Emit a private static field in each generated class to self-register metadata:
  - `private static readonly __type = registerType(Foo, { ... });`
- For enums, add a declaration-merge augmentation:
  - `export namespace Color { const __type = registerEnum(Color, { ... }); }`
- Import helpers once per TS file that uses reflection registration:
  - `import { registerType, registerEnum } from "./.net.ts";`

Feature flag
- Reflection emission is controlled by a generator flag (`EnableReflection`). When false, no reflection code is generated; the runtime remains optional.

