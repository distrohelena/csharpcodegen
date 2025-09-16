using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace cs2.ts.util
{
    // Options to toggle and customize TS reflection emission
    public sealed class TypeScriptReflectionOptions
    {
        public static readonly TypeScriptReflectionOptions Default = new TypeScriptReflectionOptions();

        // If false, no reflection code is emitted
        public bool EnableReflection { get; set; } = true;

        // Name of the private static field placed inside classes
        public string PrivateStaticFieldName { get; set; } = "__type";

        // Identifiers to call from the runtime import
        public string RegisterTypeIdent { get; set; } = "registerType";
        public string RegisterEnumIdent { get; set; } = "registerEnum";
        public string RegisterMetadataIdent { get; set; } = "registerMetadata";

        // Module path for the runtime import
        public string RuntimeImportModule { get; set; } = "./.net.ts";

        public TypeScriptReflectionOptions Clone() {
            return new TypeScriptReflectionOptions {
                EnableReflection = EnableReflection,
                PrivateStaticFieldName = PrivateStaticFieldName,
                RegisterTypeIdent = RegisterTypeIdent,
                RegisterEnumIdent = RegisterEnumIdent,
                RegisterMetadataIdent = RegisterMetadataIdent,
                RuntimeImportModule = RuntimeImportModule
            };
        }
    }

    // Emits TypeScript metadata registration for the reflection runtime in .net.ts
    // Supports two styles:
    //  1) Inline private static field initialization inside classes (preferred)
    //  2) Trailing registerType/registerEnum calls (legacy)
    public static class TypeScriptReflectionEmitter
    {
        // Global defaults that the converter can configure at startup
        public static TypeScriptReflectionOptions GlobalOptions { get; set; } = TypeScriptReflectionOptions.Default;
        private static readonly SymbolDisplayFormat FullNameFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
        );

        // Emit import for runtime helpers based on what we plan to use in this file
        // Example: import { registerType, registerEnum } from "./.net.ts";
        public static void EmitRuntimeImport(TextWriter w, bool needType, bool needEnum, bool needMetadata, TypeScriptReflectionOptions? options = null)
        {
            options ??= GlobalOptions ?? TypeScriptReflectionOptions.Default;
            if (!options.EnableReflection) return;
            var idents = new List<string>();
            if (needType) idents.Add(options.RegisterTypeIdent);
            if (needEnum) idents.Add(options.RegisterEnumIdent);
            if (needMetadata) idents.Add(options.RegisterMetadataIdent);
            if (idents.Count == 0) return;
            w.Write("import { ");
            for (int i = 0; i < idents.Count; i++)
            {
                if (i > 0) w.Write(", ");
                w.Write(idents[i]);
            }
            w.Write(" } from ");
            WriteString(w, options.RuntimeImportModule);
            w.WriteLine(";");
        }

        // Preferred: emit inside class body as a private static field initialized with type registration.
        // Usage: place the result inside the class declaration during generation.
        // Output: private static readonly __type = registerType(Foo, { ... });
        public static void EmitPrivateStaticReflectionField(TextWriter w, ITypeSymbol type, string tsClassIdentifier, TypeScriptReflectionOptions? options = null)
        {
            options ??= GlobalOptions ?? TypeScriptReflectionOptions.Default;
            if (!options.EnableReflection) return;
            var fieldName = options.PrivateStaticFieldName;
            var meta = BuildTypeMetadata(type);
            w.Write("private static readonly ");
            w.Write(fieldName);
            w.Write(" = ");
            w.Write(options.RegisterTypeIdent);
            w.Write("(");
            w.Write(tsClassIdentifier);
            w.Write(", ");
            EmitMetadataLiteral(w, meta);
            w.WriteLine(");");
        }

        // For enums (which cannot have members), emit a namespace augmentation that holds a const field for the Type.
        // Output:
        // export namespace Color { const __type = registerEnum(Color, { ... }); }
        public static void EmitEnumNamespaceReflection(TextWriter w, INamedTypeSymbol enumType, string tsEnumIdentifier, TypeScriptReflectionOptions? options = null)
        {
            options ??= GlobalOptions ?? TypeScriptReflectionOptions.Default;
            if (!options.EnableReflection) return;
            var fieldName = options.PrivateStaticFieldName;
            var meta = BuildTypeMetadata(enumType, forEnum: true);
            w.Write("export namespace ");
            w.Write(tsEnumIdentifier);
            w.WriteLine(" {");
            w.Write("    const ");
            w.Write(fieldName);
            w.Write(" = ");
            w.Write(options.RegisterEnumIdent);
            w.Write("(");
            w.Write(tsEnumIdentifier);
            w.Write(", ");
            EmitMetadataLiteral(w, meta);
            w.WriteLine(");");
            w.WriteLine("}");
        }

        public static void EmitInterfaceNamespaceReflection(TextWriter w, INamedTypeSymbol type, string tsIdentifier, TypeScriptReflectionOptions? options = null)
        {
            options ??= GlobalOptions ?? TypeScriptReflectionOptions.Default;
            if (!options.EnableReflection) return;
            var fieldName = options.PrivateStaticFieldName;
            var meta = BuildTypeMetadata(type);
            w.Write("export namespace ");
            w.Write(tsIdentifier);
            w.WriteLine(" {");
            w.Write("    const ");
            w.Write(fieldName);
            w.Write(" = ");
            w.Write(options.RegisterMetadataIdent);
            w.Write("(");
            EmitMetadataLiteral(w, meta);
            w.WriteLine(");");
            w.WriteLine("}");
        }

        // Legacy trailing call (still available if needed)
        public static void EmitRegisterForType(TextWriter w, ITypeSymbol type, string tsQualifiedName, string importIdent = "registerType")
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrWhiteSpace(tsQualifiedName)) throw new ArgumentException("TS identifier required", nameof(tsQualifiedName));
            var meta = BuildTypeMetadata(type);
            w.Write(importIdent);
            w.Write("(");
            w.Write(tsQualifiedName);
            w.Write(", ");
            EmitMetadataLiteral(w, meta);
            w.WriteLine(");");
        }

        public static void EmitRegisterForEnum(TextWriter w, INamedTypeSymbol enumType, string tsQualifiedName, string importIdent = "registerEnum")
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            if (enumType == null) throw new ArgumentNullException(nameof(enumType));
            if (string.IsNullOrWhiteSpace(tsQualifiedName)) throw new ArgumentException("TS identifier required", nameof(tsQualifiedName));
            var meta = BuildTypeMetadata(enumType, forEnum: true);
            w.Write(importIdent);
            w.Write("(");
            w.Write(tsQualifiedName);
            w.Write(", ");
            EmitMetadataLiteral(w, meta);
            w.WriteLine(");");
        }

        public static TypeMetadata BuildTypeMetadata(ITypeSymbol type, bool forEnum = false)
        {
            var name = type.Name;
            var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            var meta = new TypeMetadata
            {
                name = name,
                fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name,
                @namespace = string.IsNullOrEmpty(ns) ? null : ns,
                isEnum = type.TypeKind == TypeKind.Enum,
                isInterface = type.TypeKind == TypeKind.Interface,
                isClass = type.TypeKind == TypeKind.Class,
                isStruct = type.IsValueType && type.TypeKind == TypeKind.Struct,
                isArray = type.TypeKind == TypeKind.Array,
                isGeneric = (type as INamedTypeSymbol)?.IsGenericType ?? false,
                genericArity = (type as INamedTypeSymbol)?.Arity ?? 0,
            };

            if (meta.isEnum == true || forEnum)
            {
                meta.isEnum = true;
                meta.enumValues = BuildEnumValues(type as INamedTypeSymbol);
                return meta;
            }

            var baseType = type.BaseType;
            if (baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                meta.baseType = GetFullName(baseType);
            }

            meta.interfaces = type.Interfaces.Select(GetFullName).ToArray();
            meta.attributes = BuildAttributes(type.GetAttributes());
            meta.fields = type.GetMembers().OfType<IFieldSymbol>()
                .Where(f => !f.IsImplicitlyDeclared && !f.IsConst)
                .Select(BuildField).ToArray();
            meta.properties = type.GetMembers().OfType<IPropertySymbol>()
                .Where(p => !p.IsImplicitlyDeclared)
                .Select(BuildProperty).ToArray();
            meta.methods = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsImplicitlyDeclared)
                .Select(BuildMethod).ToArray();
            meta.constructors = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Constructor && !m.IsImplicitlyDeclared)
                .Select(BuildMethod).ToArray();

            return meta;
        }

        private static EnumValueMetadata[]? BuildEnumValues(INamedTypeSymbol? type)
        {
            if (type == null) return null;
            var underlying = type.EnumUnderlyingType;
            var values = new List<EnumValueMetadata>();
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!field.HasConstantValue || field.ConstantValue == null) continue;
                if (field.Name == "value__") continue;
                object val = field.ConstantValue;
                if (underlying != null && underlying.SpecialType != SpecialType.None)
                {
                    val = Convert.ToInt64(field.ConstantValue, CultureInfo.InvariantCulture);
                }
                values.Add(new EnumValueMetadata { name = field.Name, value = val });
            }
            return values.ToArray();
        }

        private static FieldMetadata BuildField(IFieldSymbol f) => new()
        {
            name = f.Name,
            type = GetFullName(f.Type),
            isPublic = f.DeclaredAccessibility == Accessibility.Public,
            isStatic = f.IsStatic,
            isInitOnly = f.IsReadOnly,
            attributes = BuildAttributes(f.GetAttributes()),
        };

        private static PropertyMetadata BuildProperty(IPropertySymbol p)
        {
            var isPublic = (p.GetMethod?.DeclaredAccessibility == Accessibility.Public) || (p.SetMethod?.DeclaredAccessibility == Accessibility.Public);
            return new PropertyMetadata
            {
                name = p.Name,
                type = GetFullName(p.Type),
                isPublic = isPublic,
                isStatic = p.IsStatic,
                canRead = p.GetMethod != null,
                canWrite = p.SetMethod != null,
                attributes = BuildAttributes(p.GetAttributes()),
            };
        }

        private static MethodMetadata BuildMethod(IMethodSymbol m)
        {
            return new MethodMetadata
            {
                name = m.MethodKind == MethodKind.Constructor ? ".ctor" : m.Name,
                isPublic = m.DeclaredAccessibility == Accessibility.Public,
                isStatic = m.IsStatic,
                isAbstract = m.IsAbstract,
                isVirtual = m.IsVirtual,
                returnType = m.MethodKind == MethodKind.Constructor ? null : GetFullName(m.ReturnType),
                parameters = m.Parameters.Select(BuildParam).ToArray(),
                attributes = BuildAttributes(m.GetAttributes()),
                signature = SignatureOf(m)
            };
        }

        private static ParameterMetadata BuildParam(IParameterSymbol p)
        {
            var meta = new ParameterMetadata
            {
                name = p.Name,
                type = GetFullName(p.Type),
                hasDefault = p.HasExplicitDefaultValue,
                defaultValue = p.HasExplicitDefaultValue ? ToRuntimeValue(p.ExplicitDefaultValue) : null,
                attributes = BuildAttributes(p.GetAttributes()),
            };
            return meta;
        }

        private static string SignatureOf(IMethodSymbol m)
        {
            var sb = new StringBuilder();
            sb.Append(m.Name);
            sb.Append("(");
            for (int i = 0; i < m.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(GetFullName(m.Parameters[i].Type));
            }
            sb.Append(")");
            return sb.ToString();
        }

        private static List<AttributeDataMetadata>? BuildAttributes(ImmutableArray<AttributeData> attrs)
        {
            if (attrs.Length == 0) return null;
            var list = new List<AttributeDataMetadata>();
            foreach (var a in attrs)
            {
                var item = new AttributeDataMetadata
                {
                    type = GetFullName(a.AttributeClass!),
                };
                if (a.ConstructorArguments.Length > 0)
                {
                    item.ctorArgs = a.ConstructorArguments.Select(ToRuntimeTypedConstant).ToList();
                }
                if (a.NamedArguments.Length > 0)
                {
                    item.namedArgs = a.NamedArguments.ToDictionary(k => k.Key, v => ToRuntimeTypedConstant(v.Value));
                }
                list.Add(item);
            }
            return list;
        }

        private static object? ToRuntimeTypedConstant(TypedConstant c)
        {
            if (c.IsNull) return null;
            if (c.Kind == TypedConstantKind.Array)
            {
                return c.Values.Select(ToRuntimeTypedConstant).ToArray();
            }
            return ToRuntimeValue(c.Value);
        }

        private static object? ToRuntimeValue(object? v)
        {
            return v switch
            {
                null => null,
                bool b => b,
                string s => s,
                char ch => ch.ToString(),
                sbyte sb => sb,
                byte by => by,
                short sh => sh,
                ushort ush => ush,
                int i => i,
                uint ui => ui,
                long l => l,
                ulong ul => ul,
                float f => f,
                double d => d,
                decimal m => (double)m,
                _ => v?.ToString()
            };
        }

        private static string GetFullName(ITypeSymbol t)
        {
            if (t is IArrayTypeSymbol at)
            {
                return GetFullName(at.ElementType) + "[]";
            }
            var s = t.ToDisplayString(FullNameFormat);
            return s;
        }

        private static void EmitMetadataLiteral(TextWriter w, TypeMetadata meta)
        {
            w.Write("{");
            var first = true;
            void Sep() { if (!first) w.Write(','); first = false; }

            void Prop(string key, string? value)
            {
                if (value == null) return; Sep(); w.Write('"'); w.Write(key); w.Write('"'); w.Write(':'); WriteString(w, value);
            }
            void PropB(string key, bool? value)
            {
                if (value == null) return; Sep(); w.Write('"'); w.Write(key); w.Write('"'); w.Write(':'); w.Write(value.Value ? "true" : "false");
            }
            void PropN(string key, int? value)
            {
                if (value == null) return; Sep(); w.Write('"'); w.Write(key); w.Write('"'); w.Write(':'); w.Write(value.Value.ToString(CultureInfo.InvariantCulture));
            }

            Prop("name", meta.name);
            Prop("namespace", meta.@namespace);
            Prop("fullName", meta.fullName);
            Prop("assembly", meta.assembly);
            Prop("typeId", meta.typeId);
            PropB("isClass", meta.isClass);
            PropB("isInterface", meta.isInterface);
            PropB("isStruct", meta.isStruct);
            PropB("isEnum", meta.isEnum);
            PropB("isArray", meta.isArray);
            PropB("isGeneric", meta.isGeneric);
            PropN("genericArity", meta.genericArity);
            Prop("baseType", meta.baseType);
            if (meta.interfaces != null && meta.interfaces.Length > 0) { Sep(); w.Write("\"interfaces\":"); EmitArray(w, meta.interfaces, WriteString); }
            if (meta.attributes != null && meta.attributes.Count > 0) { Sep(); w.Write("\"attributes\":"); EmitArray(w, meta.attributes, EmitAttribute); }
            if (meta.fields != null && meta.fields.Length > 0) { Sep(); w.Write("\"fields\":"); EmitArray(w, meta.fields, EmitField); }
            if (meta.properties != null && meta.properties.Length > 0) { Sep(); w.Write("\"properties\":"); EmitArray(w, meta.properties, EmitProperty); }
            if (meta.methods != null && meta.methods.Length > 0) { Sep(); w.Write("\"methods\":"); EmitArray(w, meta.methods, EmitMethod); }
            if (meta.constructors != null && meta.constructors.Length > 0) { Sep(); w.Write("\"constructors\":"); EmitArray(w, meta.constructors, EmitMethod); }
            if (meta.enumValues != null && meta.enumValues.Length > 0) { Sep(); w.Write("\"enumValues\":"); EmitArray(w, meta.enumValues, EmitEnumValue); }
            Prop("elementType", meta.elementType);

            w.Write("}");
        }

        private static void EmitArray<T>(TextWriter w, IReadOnlyList<T> items, Action<TextWriter, T> emit)
        {
            w.Write('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) w.Write(',');
                emit(w, items[i]);
            }
            w.Write(']');
        }

        private static void WriteString(TextWriter w, string s)
        {
            w.Write('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': w.Write("\\\""); break;
                    case '\\': w.Write("\\\\"); break;
                    case '\n': w.Write("\\n"); break;
                    case '\r': w.Write("\\r"); break;
                    case '\t': w.Write("\\t"); break;
                    default: w.Write(ch); break;
                }
            }
            w.Write('"');
        }

        private static void EmitAttribute(TextWriter w, AttributeDataMetadata a)
        {
            w.Write('{');
            var first = true; void Sep() { if (!first) w.Write(','); first = false; }
            Sep(); w.Write("\"type\":"); WriteString(w, a.type);
            if (a.ctorArgs != null && a.ctorArgs.Count > 0)
            { Sep(); w.Write("\"ctorArgs\":"); EmitArray(w, a.ctorArgs, EmitValue); }
            if (a.namedArgs != null && a.namedArgs.Count > 0)
            {
                Sep(); w.Write("\"namedArgs\":{");
                var i = 0; foreach (var kv in a.namedArgs)
                {
                    if (i++ > 0) w.Write(',');
                    WriteString(w, kv.Key); w.Write(':'); EmitValue(w, kv.Value);
                }
                w.Write('}');
            }
            w.Write('}');
        }

        private static void EmitEnumValue(TextWriter w, EnumValueMetadata v)
        {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, v.name);
            w.Write(','); w.Write("\"value\":"); EmitValue(w, v.value);
            w.Write('}');
        }

        private static void EmitField(TextWriter w, FieldMetadata f)
        {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, f.name);
            w.Write(','); w.Write("\"type\":"); WriteString(w, f.type);
            if (f.isPublic != null) { w.Write(','); w.Write("\"isPublic\":"); w.Write(f.isPublic.Value ? "true" : "false"); }
            if (f.isStatic != null) { w.Write(','); w.Write("\"isStatic\":"); w.Write(f.isStatic.Value ? "true" : "false"); }
            if (f.isInitOnly != null) { w.Write(','); w.Write("\"isInitOnly\":"); w.Write(f.isInitOnly.Value ? "true" : "false"); }
            if (f.attributes != null && f.attributes.Count > 0) { w.Write(','); w.Write("\"attributes\":"); EmitArray(w, f.attributes, EmitAttribute); }
            w.Write('}');
        }

        private static void EmitProperty(TextWriter w, PropertyMetadata p)
        {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, p.name);
            w.Write(','); w.Write("\"type\":"); WriteString(w, p.type);
            if (p.isPublic != null) { w.Write(','); w.Write("\"isPublic\":"); w.Write(p.isPublic.Value ? "true" : "false"); }
            if (p.isStatic != null) { w.Write(','); w.Write("\"isStatic\":"); w.Write(p.isStatic.Value ? "true" : "false"); }
            if (p.canRead != null) { w.Write(','); w.Write("\"canRead\":"); w.Write(p.canRead.Value ? "true" : "false"); }
            if (p.canWrite != null) { w.Write(','); w.Write("\"canWrite\":"); w.Write(p.canWrite.Value ? "true" : "false"); }
            if (p.attributes != null && p.attributes.Count > 0) { w.Write(','); w.Write("\"attributes\":"); EmitArray(w, p.attributes, EmitAttribute); }
            w.Write('}');
        }

        private static void EmitMethod(TextWriter w, MethodMetadata m)
        {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, m.name);
            if (m.isPublic != null) { w.Write(','); w.Write("\"isPublic\":"); w.Write(m.isPublic.Value ? "true" : "false"); }
            if (m.isStatic != null) { w.Write(','); w.Write("\"isStatic\":"); w.Write(m.isStatic.Value ? "true" : "false"); }
            if (m.isAbstract != null) { w.Write(','); w.Write("\"isAbstract\":"); w.Write(m.isAbstract.Value ? "true" : "false"); }
            if (m.isVirtual != null) { w.Write(','); w.Write("\"isVirtual\":"); w.Write(m.isVirtual.Value ? "true" : "false"); }
            if (!string.IsNullOrEmpty(m.returnType)) { w.Write(','); w.Write("\"returnType\":"); WriteString(w, m.returnType!); }
            if (m.parameters != null && m.parameters.Length > 0) { w.Write(','); w.Write("\"parameters\":"); EmitArray(w, m.parameters, EmitParameter); }
            if (m.attributes != null && m.attributes.Count > 0) { w.Write(','); w.Write("\"attributes\":"); EmitArray(w, m.attributes, EmitAttribute); }
            if (!string.IsNullOrEmpty(m.signature)) { w.Write(','); w.Write("\"signature\":"); WriteString(w, m.signature); }
            w.Write('}');
        }

        private static void EmitParameter(TextWriter w, ParameterMetadata p)
        {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, p.name);
            w.Write(','); w.Write("\"type\":"); WriteString(w, p.type);
            if (p.hasDefault != null) { w.Write(','); w.Write("\"hasDefault\":"); w.Write(p.hasDefault.Value ? "true" : "false"); }
            if (p.hasDefault == true) { w.Write(','); w.Write("\"defaultValue\":"); EmitValue(w, p.defaultValue); }
            if (p.attributes != null && p.attributes.Count > 0) { w.Write(','); w.Write("\"attributes\":"); EmitArray(w, p.attributes, EmitAttribute); }
            w.Write('}');
        }

        private static void EmitValue(TextWriter w, object? v)
        {
            switch (v)
            {
                case null:
                    w.Write("null");
                    break;
                case bool b:
                    w.Write(b ? "true" : "false");
                    break;
                case string s:
                    WriteString(w, s);
                    break;
                case char ch:
                    WriteString(w, ch.ToString());
                    break;
                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    w.Write(Convert.ToString(v, CultureInfo.InvariantCulture));
                    break;
                case IEnumerable<object?> arr:
                    var list = arr.ToList();
                    w.Write('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) w.Write(',');
                        EmitValue(w, list[i]);
                    }
                    w.Write(']');
                    break;
                default:
                    WriteString(w, v?.ToString() ?? string.Empty);
                    break;
            }
        }
    }

    // DTOs for metadata shape (align to .net.ts runtime TypeMetadata)
    public sealed class TypeMetadata
    {
        public string name { get; set; } = string.Empty;
        public string? @namespace { get; set; }
        public string fullName { get; set; } = string.Empty;
        public string? assembly { get; set; }
        public string? typeId { get; set; }
        public bool? isClass { get; set; }
        public bool? isInterface { get; set; }
        public bool? isStruct { get; set; }
        public bool? isEnum { get; set; }
        public bool? isArray { get; set; }
        public bool? isGeneric { get; set; }
        public int? genericArity { get; set; }
        public string? baseType { get; set; }
        public string[]? interfaces { get; set; }
        public List<AttributeDataMetadata>? attributes { get; set; }
        public FieldMetadata[]? fields { get; set; }
        public PropertyMetadata[]? properties { get; set; }
        public MethodMetadata[]? methods { get; set; }
        public MethodMetadata[]? constructors { get; set; }
        public EnumValueMetadata[]? enumValues { get; set; }
        public string? elementType { get; set; }
    }

    public sealed class AttributeDataMetadata
    {
        public string type { get; set; } = string.Empty;
        public List<object?>? ctorArgs { get; set; }
        public Dictionary<string, object?>? namedArgs { get; set; }
    }

    public sealed class FieldMetadata
    {
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public bool? isPublic { get; set; }
        public bool? isStatic { get; set; }
        public bool? isInitOnly { get; set; }
        public List<AttributeDataMetadata>? attributes { get; set; }
    }

    public sealed class PropertyMetadata
    {
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public bool? isPublic { get; set; }
        public bool? isStatic { get; set; }
        public bool? canRead { get; set; }
        public bool? canWrite { get; set; }
        public List<AttributeDataMetadata>? attributes { get; set; }
    }

    public sealed class MethodMetadata
    {
        public string name { get; set; } = string.Empty;
        public bool? isPublic { get; set; }
        public bool? isStatic { get; set; }
        public bool? isAbstract { get; set; }
        public bool? isVirtual { get; set; }
        public string? returnType { get; set; }
        public ParameterMetadata[]? parameters { get; set; }
        public List<AttributeDataMetadata>? attributes { get; set; }
        public string? signature { get; set; }
    }

    public sealed class ParameterMetadata
    {
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public bool? hasDefault { get; set; }
        public object? defaultValue { get; set; }
        public List<AttributeDataMetadata>? attributes { get; set; }
    }

    public sealed class EnumValueMetadata
    {
        public string name { get; set; } = string.Empty;
        public object? value { get; set; }
    }
}
