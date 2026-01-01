using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace cs2.ts.util {
    /// <summary>
    /// Emits TypeScript metadata registration for the reflection runtime in .net.ts.
    /// </summary>
    public static class TypeScriptReflectionEmitter {
        /// <summary>
        /// Symbol display format used to render fully-qualified type names.
        /// </summary>
        static readonly SymbolDisplayFormat FullNameFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
        );

        /// <summary>
        /// Gets or sets the global reflection options used when no overrides are provided.
        /// </summary>
        public static ReflectionOptions GlobalOptions { get; set; } = ReflectionOptions.Default;

        /// <summary>
        /// Resolves reflection options using explicit overrides or global defaults.
        /// </summary>
        /// <param name="options">The caller-provided options.</param>
        /// <returns>The resolved options to use for emission.</returns>
        static ReflectionOptions ResolveOptions(ReflectionOptions options) {
            if (options != null) {
                return options;
            }
            if (GlobalOptions != null) {
                return GlobalOptions;
            }
            return ReflectionOptions.Default;
        }

        /// <summary>
        /// Emits the import statement for reflection runtime helpers used in the file.
        /// </summary>
        /// <param name="w">The writer receiving the import statement.</param>
        /// <param name="needType">Whether type registration is needed.</param>
        /// <param name="needEnum">Whether enum registration is needed.</param>
        /// <param name="needMetadata">Whether metadata-only registration is needed.</param>
        /// <param name="options">Optional reflection overrides to apply.</param>
        public static void EmitRuntimeImport(TextWriter w, bool needType, bool needEnum, bool needMetadata, ReflectionOptions options = null) {
            options = ResolveOptions(options);
            if (!options.EnableReflection) return;
            var idents = new List<string>();
            if (needType) idents.Add(options.RegisterTypeIdent);
            if (needEnum) idents.Add(options.RegisterEnumIdent);
            if (needMetadata) idents.Add(options.RegisterMetadataIdent);
            if (idents.Count == 0) return;
            w.Write("import { ");
            for (int i = 0; i < idents.Count; i++) {
                if (i > 0) w.Write(", ");
                w.Write(idents[i]);
            }
            w.Write(" } from ");
            WriteString(w, options.RuntimeImportModule);
            w.WriteLine(";");
        }

        /// <summary>
        /// Emits a private static field that registers type metadata inside a class body.
        /// </summary>
        /// <param name="w">The writer receiving the field declaration.</param>
        /// <param name="type">The Roslyn symbol for the type being emitted.</param>
        /// <param name="tsClassIdentifier">The TypeScript class identifier.</param>
        /// <param name="options">Optional reflection overrides to apply.</param>
        public static void EmitPrivateStaticReflectionField(TextWriter w, ITypeSymbol type, string tsClassIdentifier, ReflectionOptions options = null) {
            options = ResolveOptions(options);
            if (!options.EnableReflection) return;
            var fieldName = options.PrivateStaticFieldName;
            var meta = BuildTypeMetadata(type);
            w.Write("static readonly ");
            w.Write(fieldName);
            w.Write(" = ");
            w.Write(options.RegisterTypeIdent);
            w.Write("(");
            w.Write(tsClassIdentifier);
            w.Write(", ");
            EmitMetadataLiteral(w, meta);
            w.WriteLine(");");
        }

        /// <summary>
        /// Emits a namespace augmentation that registers enum metadata.
        /// </summary>
        /// <param name="w">The writer receiving the namespace augmentation.</param>
        /// <param name="enumType">The Roslyn symbol for the enum.</param>
        /// <param name="tsEnumIdentifier">The TypeScript enum identifier.</param>
        /// <param name="options">Optional reflection overrides to apply.</param>
        public static void EmitEnumNamespaceReflection(TextWriter w, INamedTypeSymbol enumType, string tsEnumIdentifier, ReflectionOptions options = null) {
            options = ResolveOptions(options);
            if (!options.EnableReflection) return;
            var fieldName = options.PrivateStaticFieldName;
            var meta = BuildTypeMetadata(enumType, forEnum: true);
            w.Write("export namespace ");
            w.Write(tsEnumIdentifier);
            w.WriteLine(" {");
            w.Write("const ");
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

        /// <summary>
        /// Emits a namespace augmentation that registers interface metadata.
        /// </summary>
        /// <param name="w">The writer receiving the namespace augmentation.</param>
        /// <param name="type">The Roslyn symbol for the interface.</param>
        /// <param name="tsIdentifier">The TypeScript interface identifier.</param>
        /// <param name="options">Optional reflection overrides to apply.</param>
        public static void EmitInterfaceNamespaceReflection(TextWriter w, INamedTypeSymbol type, string tsIdentifier, ReflectionOptions options = null) {
            options = ResolveOptions(options);
            if (!options.EnableReflection) return;
            var fieldName = options.PrivateStaticFieldName;
            var meta = BuildTypeMetadata(type);
            w.Write("export namespace ");
            w.Write(tsIdentifier);
            w.WriteLine(" {");
            w.Write("const ");
            w.Write(fieldName);
            w.Write(" = ");
            w.Write(options.RegisterMetadataIdent);
            w.Write("(");
            EmitMetadataLiteral(w, meta);
            w.WriteLine(");");
            w.WriteLine("}");
        }

        /// <summary>
        /// Emits a trailing type registration call (legacy output style).
        /// </summary>
        /// <param name="w">The writer receiving the registration call.</param>
        /// <param name="type">The Roslyn symbol for the type.</param>
        /// <param name="tsQualifiedName">The fully-qualified TypeScript identifier.</param>
        /// <param name="importIdent">The runtime import identifier to call.</param>
        public static void EmitRegisterForType(TextWriter w, ITypeSymbol type, string tsQualifiedName, string importIdent = "registerType") {
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

        /// <summary>
        /// Emits a trailing enum registration call (legacy output style).
        /// </summary>
        /// <param name="w">The writer receiving the registration call.</param>
        /// <param name="enumType">The Roslyn symbol for the enum.</param>
        /// <param name="tsQualifiedName">The fully-qualified TypeScript identifier.</param>
        /// <param name="importIdent">The runtime import identifier to call.</param>
        public static void EmitRegisterForEnum(TextWriter w, INamedTypeSymbol enumType, string tsQualifiedName, string importIdent = "registerEnum") {
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

        /// <summary>
        /// Builds reflection metadata for the given type symbol.
        /// </summary>
        /// <param name="type">The Roslyn symbol for the type.</param>
        /// <param name="forEnum">Whether to force enum metadata output.</param>
        /// <returns>The populated metadata descriptor.</returns>
        public static TypeMetadata BuildTypeMetadata(ITypeSymbol type, bool forEnum = false) {
            var name = type.Name;
            string ns = string.Empty;
            var containingNamespace = type.ContainingNamespace;
            if (containingNamespace != null) {
                ns = containingNamespace.ToDisplayString();
            }

            INamedTypeSymbol namedType = null;
            bool isGeneric = false;
            int genericArity = 0;
            if (type is INamedTypeSymbol namedTypeSymbol) {
                namedType = namedTypeSymbol;
                isGeneric = namedTypeSymbol.IsGenericType;
                genericArity = namedTypeSymbol.Arity;
            }

            var meta = new TypeMetadata {
                Name = name,
                FullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name,
                Namespace = string.IsNullOrEmpty(ns) ? null : ns,
                IsEnum = type.TypeKind == TypeKind.Enum,
                IsInterface = type.TypeKind == TypeKind.Interface,
                IsClass = type.TypeKind == TypeKind.Class,
                IsStruct = type.IsValueType && type.TypeKind == TypeKind.Struct,
                IsArray = type.TypeKind == TypeKind.Array,
                IsGeneric = isGeneric,
                GenericArity = genericArity,
            };

            if (meta.IsEnum == true || forEnum) {
                meta.IsEnum = true;
                meta.EnumValues = BuildEnumValues(namedType);
                return meta;
            }

            var baseType = type.BaseType;
            if (baseType != null && baseType.SpecialType != SpecialType.System_Object) {
                meta.BaseType = GetFullName(baseType);
            }

            meta.Interfaces = type.Interfaces.Select(GetFullName).ToArray();
            meta.Attributes = BuildAttributes(type.GetAttributes());
            meta.Fields = type.GetMembers().OfType<IFieldSymbol>()
                .Where(f => !f.IsImplicitlyDeclared && !f.IsConst)
                .Select(BuildField).ToArray();
            meta.Properties = type.GetMembers().OfType<IPropertySymbol>()
                .Where(p => !p.IsImplicitlyDeclared)
                .Select(BuildProperty).ToArray();
            meta.Methods = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsImplicitlyDeclared)
                .Select(BuildMethod).ToArray();
            meta.Constructors = type.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Constructor && !m.IsImplicitlyDeclared)
                .Select(BuildMethod).ToArray();

            return meta;
        }

        /// <summary>
        /// Builds enum value metadata for a named enum symbol.
        /// </summary>
        /// <param name="type">The enum symbol.</param>
        /// <returns>The enum value metadata array.</returns>
        static EnumValueMetadata[] BuildEnumValues(INamedTypeSymbol type) {
            if (type == null) return null;
            var underlying = type.EnumUnderlyingType;
            var values = new List<EnumValueMetadata>();
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>()) {
                if (!field.HasConstantValue || field.ConstantValue == null) continue;
                if (field.Name == "value__") continue;
                object val = field.ConstantValue;
                if (underlying != null && underlying.SpecialType != SpecialType.None) {
                    val = Convert.ToInt64(field.ConstantValue, CultureInfo.InvariantCulture);
                }
                values.Add(new EnumValueMetadata { Name = field.Name, Value = val });
            }
            return values.ToArray();
        }

        /// <summary>
        /// Builds field metadata for a field symbol.
        /// </summary>
        /// <param name="f">The field symbol.</param>
        /// <returns>The field metadata.</returns>
        static FieldMetadata BuildField(IFieldSymbol f) => new() {
            Name = f.Name,
            Type = GetFullName(f.Type),
            IsPublic = f.DeclaredAccessibility == Accessibility.Public,
            IsStatic = f.IsStatic,
            IsInitOnly = f.IsReadOnly,
            Attributes = BuildAttributes(f.GetAttributes()),
        };

        /// <summary>
        /// Builds property metadata for a property symbol.
        /// </summary>
        /// <param name="p">The property symbol.</param>
        /// <returns>The property metadata.</returns>
        static PropertyMetadata BuildProperty(IPropertySymbol p) {
            bool isPublic = false;
            if (p.GetMethod != null && p.GetMethod.DeclaredAccessibility == Accessibility.Public) {
                isPublic = true;
            }
            if (p.SetMethod != null && p.SetMethod.DeclaredAccessibility == Accessibility.Public) {
                isPublic = true;
            }
            return new PropertyMetadata {
                Name = p.Name,
                Type = GetFullName(p.Type),
                IsPublic = isPublic,
                IsStatic = p.IsStatic,
                CanRead = p.GetMethod != null,
                CanWrite = p.SetMethod != null,
                Attributes = BuildAttributes(p.GetAttributes()),
            };
        }

        /// <summary>
        /// Builds method metadata for a method symbol.
        /// </summary>
        /// <param name="m">The method symbol.</param>
        /// <returns>The method metadata.</returns>
        static MethodMetadata BuildMethod(IMethodSymbol m) {
            return new MethodMetadata {
                Name = m.MethodKind == MethodKind.Constructor ? ".ctor" : m.Name,
                IsPublic = m.DeclaredAccessibility == Accessibility.Public,
                IsStatic = m.IsStatic,
                IsAbstract = m.IsAbstract,
                IsVirtual = m.IsVirtual,
                ReturnType = m.MethodKind == MethodKind.Constructor ? null : GetFullName(m.ReturnType),
                Parameters = m.Parameters.Select(BuildParam).ToArray(),
                Attributes = BuildAttributes(m.GetAttributes()),
                Signature = SignatureOf(m)
            };
        }

        /// <summary>
        /// Builds parameter metadata for a parameter symbol.
        /// </summary>
        /// <param name="p">The parameter symbol.</param>
        /// <returns>The parameter metadata.</returns>
        static ParameterMetadata BuildParam(IParameterSymbol p) {
            var meta = new ParameterMetadata {
                Name = p.Name,
                Type = GetFullName(p.Type),
                HasDefault = p.HasExplicitDefaultValue,
                DefaultValue = p.HasExplicitDefaultValue ? ToRuntimeValue(p.ExplicitDefaultValue) : null,
                Attributes = BuildAttributes(p.GetAttributes()),
            };
            return meta;
        }

        /// <summary>
        /// Computes a signature string for a method symbol.
        /// </summary>
        /// <param name="m">The method symbol.</param>
        /// <returns>The signature string.</returns>
        static string SignatureOf(IMethodSymbol m) {
            var sb = new StringBuilder();
            sb.Append(m.Name);
            sb.Append("(");
            for (int i = 0; i < m.Parameters.Length; i++) {
                if (i > 0) sb.Append(",");
                sb.Append(GetFullName(m.Parameters[i].Type));
            }
            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// Builds attribute metadata for a set of Roslyn attributes.
        /// </summary>
        /// <param name="attrs">The attribute data collection.</param>
        /// <returns>The attribute metadata list.</returns>
        static List<AttributeDataMetadata> BuildAttributes(ImmutableArray<AttributeData> attrs) {
            if (attrs.Length == 0) return null;
            var list = new List<AttributeDataMetadata>();
            foreach (var a in attrs) {
                if (a.AttributeClass == null) {
                    continue;
                }

                var item = new AttributeDataMetadata {
                    Type = GetFullName(a.AttributeClass),
                };
                if (a.ConstructorArguments.Length > 0) {
                    item.CtorArgs = a.ConstructorArguments.Select(ToRuntimeTypedConstant).ToList();
                }
                if (a.NamedArguments.Length > 0) {
                    item.NamedArgs = a.NamedArguments.ToDictionary(k => k.Key, v => ToRuntimeTypedConstant(v.Value));
                }
                list.Add(item);
            }
            return list;
        }

        /// <summary>
        /// Converts a typed constant into a runtime-friendly value.
        /// </summary>
        /// <param name="c">The typed constant.</param>
        /// <returns>The runtime value representation.</returns>
        static object ToRuntimeTypedConstant(TypedConstant c) {
            if (c.IsNull) return null;
            if (c.Kind == TypedConstantKind.Array) {
                return c.Values.Select(ToRuntimeTypedConstant).ToArray();
            }
            return ToRuntimeValue(c.Value);
        }

        /// <summary>
        /// Normalizes a runtime value for metadata serialization.
        /// </summary>
        /// <param name="v">The raw value.</param>
        /// <returns>The normalized value.</returns>
        static object ToRuntimeValue(object v) {
            return v switch {
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
                _ => v.ToString()
            };
        }

        /// <summary>
        /// Gets the fully-qualified name for a Roslyn type symbol.
        /// </summary>
        /// <param name="t">The type symbol.</param>
        /// <returns>The fully-qualified type name.</returns>
        static string GetFullName(ITypeSymbol t) {
            if (t is IArrayTypeSymbol at) {
                return GetFullName(at.ElementType) + "[]";
            }
            var s = t.ToDisplayString(FullNameFormat);
            return s;
        }

        /// <summary>
        /// Emits a metadata literal object for the reflection runtime.
        /// </summary>
        /// <param name="w">The writer receiving the literal.</param>
        /// <param name="meta">The metadata to serialize.</param>
        static void EmitMetadataLiteral(TextWriter w, TypeMetadata meta) {
            w.Write("{");
            var first = true;
            WriteStringProperty(w, ref first, "name", meta.Name);
            WriteStringProperty(w, ref first, "namespace", meta.Namespace);
            WriteStringProperty(w, ref first, "fullName", meta.FullName);
            WriteStringProperty(w, ref first, "assembly", meta.Assembly);
            WriteStringProperty(w, ref first, "typeId", meta.TypeId);
            WriteBoolProperty(w, ref first, "isClass", meta.IsClass);
            WriteBoolProperty(w, ref first, "isInterface", meta.IsInterface);
            WriteBoolProperty(w, ref first, "isStruct", meta.IsStruct);
            WriteBoolProperty(w, ref first, "isEnum", meta.IsEnum);
            WriteBoolProperty(w, ref first, "isArray", meta.IsArray);
            WriteBoolProperty(w, ref first, "isGeneric", meta.IsGeneric);
            WriteIntProperty(w, ref first, "genericArity", meta.GenericArity);
            WriteStringProperty(w, ref first, "baseType", meta.BaseType);
            if (meta.Interfaces != null && meta.Interfaces.Length > 0) { WriteSeparator(w, ref first); w.Write("\"interfaces\":"); EmitArray(w, meta.Interfaces, WriteString); }
            if (meta.Attributes != null && meta.Attributes.Count > 0) { WriteSeparator(w, ref first); w.Write("\"attributes\":"); EmitArray(w, meta.Attributes, EmitAttribute); }
            if (meta.Fields != null && meta.Fields.Length > 0) { WriteSeparator(w, ref first); w.Write("\"fields\":"); EmitArray(w, meta.Fields, EmitField); }
            if (meta.Properties != null && meta.Properties.Length > 0) { WriteSeparator(w, ref first); w.Write("\"properties\":"); EmitArray(w, meta.Properties, EmitProperty); }
            if (meta.Methods != null && meta.Methods.Length > 0) { WriteSeparator(w, ref first); w.Write("\"methods\":"); EmitArray(w, meta.Methods, EmitMethod); }
            if (meta.Constructors != null && meta.Constructors.Length > 0) { WriteSeparator(w, ref first); w.Write("\"constructors\":"); EmitArray(w, meta.Constructors, EmitMethod); }
            if (meta.EnumValues != null && meta.EnumValues.Length > 0) { WriteSeparator(w, ref first); w.Write("\"enumValues\":"); EmitArray(w, meta.EnumValues, EmitEnumValue); }
            WriteStringProperty(w, ref first, "elementType", meta.ElementType);

            w.Write("}");
        }

        /// <summary>
        /// Emits a JSON-like array using the provided element emitter.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="w">The writer receiving the array.</param>
        /// <param name="items">The items to emit.</param>
        /// <param name="emit">The per-item emitter.</param>
        static void EmitArray<T>(TextWriter w, IReadOnlyList<T> items, Action<TextWriter, T> emit) {
            w.Write('[');
            for (int i = 0; i < items.Count; i++) {
                if (i > 0) w.Write(',');
                emit(w, items[i]);
            }
            w.Write(']');
        }

        /// <summary>
        /// Writes a JSON-escaped string literal.
        /// </summary>
        /// <param name="w">The writer receiving the string.</param>
        /// <param name="s">The string value to escape.</param>
        static void WriteString(TextWriter w, string s) {
            w.Write('"');
            foreach (var ch in s) {
                switch (ch) {
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

        /// <summary>
        /// Emits attribute metadata as a JSON-like object.
        /// </summary>
        /// <param name="w">The writer receiving the attribute data.</param>
        /// <param name="a">The attribute metadata.</param>
        static void EmitAttribute(TextWriter w, AttributeDataMetadata a) {
            w.Write('{');
            var first = true;
            WriteSeparator(w, ref first); w.Write("\"type\":"); WriteString(w, a.Type);
            if (a.CtorArgs != null && a.CtorArgs.Count > 0)
            { WriteSeparator(w, ref first); w.Write("\"ctorArgs\":"); EmitArray(w, a.CtorArgs, EmitValue); }
            if (a.NamedArgs != null && a.NamedArgs.Count > 0) {
                WriteSeparator(w, ref first); w.Write("\"namedArgs\":{");
                var i = 0; foreach (var kv in a.NamedArgs) {
                    if (i++ > 0) w.Write(',');
                    WriteString(w, kv.Key); w.Write(':'); EmitValue(w, kv.Value);
                }
                w.Write('}');
            }
            w.Write('}');
        }

        /// <summary>
        /// Emits enum value metadata as a JSON-like object.
        /// </summary>
        /// <param name="w">The writer receiving the enum value.</param>
        /// <param name="v">The enum value metadata.</param>
        static void EmitEnumValue(TextWriter w, EnumValueMetadata v) {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, v.Name);
            w.Write(','); w.Write("\"value\":"); EmitValue(w, v.Value);
            w.Write('}');
        }

        /// <summary>
        /// Emits field metadata as a JSON-like object.
        /// </summary>
        /// <param name="w">The writer receiving the field data.</param>
        /// <param name="f">The field metadata.</param>
        static void EmitField(TextWriter w, FieldMetadata f) {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, f.Name);
            w.Write(','); w.Write("\"type\":"); WriteString(w, f.Type);
            if (f.IsPublic != null) { w.Write(','); w.Write("\"isPublic\":"); w.Write(f.IsPublic.Value ? "true" : "false"); }
            if (f.IsStatic != null) { w.Write(','); w.Write("\"isStatic\":"); w.Write(f.IsStatic.Value ? "true" : "false"); }
            if (f.IsInitOnly != null) { w.Write(','); w.Write("\"isInitOnly\":"); w.Write(f.IsInitOnly.Value ? "true" : "false"); }
            if (f.Attributes != null && f.Attributes.Count > 0) { w.Write(','); w.Write("\"attributes\":"); EmitArray(w, f.Attributes, EmitAttribute); }
            w.Write('}');
        }

        /// <summary>
        /// Emits property metadata as a JSON-like object.
        /// </summary>
        /// <param name="w">The writer receiving the property data.</param>
        /// <param name="p">The property metadata.</param>
        static void EmitProperty(TextWriter w, PropertyMetadata p) {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, p.Name);
            w.Write(','); w.Write("\"type\":"); WriteString(w, p.Type);
            if (p.IsPublic != null) { w.Write(','); w.Write("\"isPublic\":"); w.Write(p.IsPublic.Value ? "true" : "false"); }
            if (p.IsStatic != null) { w.Write(','); w.Write("\"isStatic\":"); w.Write(p.IsStatic.Value ? "true" : "false"); }
            if (p.CanRead != null) { w.Write(','); w.Write("\"canRead\":"); w.Write(p.CanRead.Value ? "true" : "false"); }
            if (p.CanWrite != null) { w.Write(','); w.Write("\"canWrite\":"); w.Write(p.CanWrite.Value ? "true" : "false"); }
            if (p.Attributes != null && p.Attributes.Count > 0) { w.Write(','); w.Write("\"attributes\":"); EmitArray(w, p.Attributes, EmitAttribute); }
            w.Write('}');
        }

        /// <summary>
        /// Emits method metadata as a JSON-like object.
        /// </summary>
        /// <param name="w">The writer receiving the method data.</param>
        /// <param name="m">The method metadata.</param>
        static void EmitMethod(TextWriter w, MethodMetadata m) {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, m.Name);
            if (m.IsPublic != null) { w.Write(','); w.Write("\"isPublic\":"); w.Write(m.IsPublic.Value ? "true" : "false"); }
            if (m.IsStatic != null) { w.Write(','); w.Write("\"isStatic\":"); w.Write(m.IsStatic.Value ? "true" : "false"); }
            if (m.IsAbstract != null) { w.Write(','); w.Write("\"isAbstract\":"); w.Write(m.IsAbstract.Value ? "true" : "false"); }
            if (m.IsVirtual != null) { w.Write(','); w.Write("\"isVirtual\":"); w.Write(m.IsVirtual.Value ? "true" : "false"); }
            if (!string.IsNullOrEmpty(m.ReturnType)) { w.Write(','); w.Write("\"returnType\":"); WriteString(w, m.ReturnType); }
            if (m.Parameters != null && m.Parameters.Length > 0) { w.Write(','); w.Write("\"parameters\":"); EmitArray(w, m.Parameters, EmitParameter); }
            if (m.Attributes != null && m.Attributes.Count > 0) { w.Write(','); w.Write("\"attributes\":"); EmitArray(w, m.Attributes, EmitAttribute); }
            if (!string.IsNullOrEmpty(m.Signature)) { w.Write(','); w.Write("\"signature\":"); WriteString(w, m.Signature); }
            w.Write('}');
        }

        /// <summary>
        /// Emits parameter metadata as a JSON-like object.
        /// </summary>
        /// <param name="w">The writer receiving the parameter data.</param>
        /// <param name="p">The parameter metadata.</param>
        static void EmitParameter(TextWriter w, ParameterMetadata p) {
            w.Write('{');
            w.Write("\"name\":"); WriteString(w, p.Name);
            w.Write(','); w.Write("\"type\":"); WriteString(w, p.Type);
            if (p.HasDefault != null) { w.Write(','); w.Write("\"hasDefault\":"); w.Write(p.HasDefault.Value ? "true" : "false"); }
            if (p.HasDefault == true) { w.Write(','); w.Write("\"defaultValue\":"); EmitValue(w, p.DefaultValue); }
            if (p.Attributes != null && p.Attributes.Count > 0) { w.Write(','); w.Write("\"attributes\":"); EmitArray(w, p.Attributes, EmitAttribute); }
            w.Write('}');
        }

        /// <summary>
        /// Emits a primitive or array value literal for metadata serialization.
        /// </summary>
        /// <param name="w">The writer receiving the value.</param>
        /// <param name="v">The value to emit.</param>
        static void EmitValue(TextWriter w, object v) {
            switch (v) {
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
                case IEnumerable<object> arr:
                    var list = arr.ToList();
                    w.Write('[');
                    for (int i = 0; i < list.Count; i++) {
                        if (i > 0) w.Write(',');
                        EmitValue(w, list[i]);
                }
                    w.Write(']');
                    break;
                default:
                    string value = v.ToString();
                    if (value == null) {
                        value = string.Empty;
                }
                    WriteString(w, value);
                    break;
            }
        }

        /// <summary>
        /// Writes a comma separator when emitting object literals.
        /// </summary>
        /// <param name="w">The writer receiving the separator.</param>
        /// <param name="first">Tracks whether a value has been written.</param>
        static void WriteSeparator(TextWriter w, ref bool first) {
            if (!first) {
                w.Write(',');
            }
            first = false;
        }

        /// <summary>
        /// Writes a string property if the value is not null.
        /// </summary>
        /// <param name="w">The writer receiving the property.</param>
        /// <param name="first">Tracks whether a value has been written.</param>
        /// <param name="key">The property name.</param>
        /// <param name="value">The property value.</param>
        static void WriteStringProperty(TextWriter w, ref bool first, string key, string value) {
            if (value == null) {
                return;
            }

            WriteSeparator(w, ref first);
            w.Write('"');
            w.Write(key);
            w.Write('"');
            w.Write(':');
            WriteString(w, value);
        }

        /// <summary>
        /// Writes a boolean property if the value is set.
        /// </summary>
        /// <param name="w">The writer receiving the property.</param>
        /// <param name="first">Tracks whether a value has been written.</param>
        /// <param name="key">The property name.</param>
        /// <param name="value">The property value.</param>
        static void WriteBoolProperty(TextWriter w, ref bool first, string key, bool? value) {
            if (value == null) {
                return;
            }

            WriteSeparator(w, ref first);
            w.Write('"');
            w.Write(key);
            w.Write('"');
            w.Write(':');
            w.Write(value.Value ? "true" : "false");
        }

        /// <summary>
        /// Writes an integer property if the value is set.
        /// </summary>
        /// <param name="w">The writer receiving the property.</param>
        /// <param name="first">Tracks whether a value has been written.</param>
        /// <param name="key">The property name.</param>
        /// <param name="value">The property value.</param>
        static void WriteIntProperty(TextWriter w, ref bool first, string key, int? value) {
            if (value == null) {
                return;
            }

            WriteSeparator(w, ref first);
            w.Write('"');
            w.Write(key);
            w.Write('"');
            w.Write(':');
            w.Write(value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

}
