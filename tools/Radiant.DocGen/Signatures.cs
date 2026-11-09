using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace com.radiant.tools.docgen;

/// <summary>Formats declarations as C# token streams whose type and member names link to their pages.</summary>
public sealed class Signatures
{
    const SymbolDisplayMiscellaneousOptions Miscellaneous =
        SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
        SymbolDisplayMiscellaneousOptions.AllowDefaultLiteral;

    static readonly SymbolDisplayFormat MemberFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints | SymbolDisplayGenericsOptions.IncludeVariance,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers | SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeRef | SymbolDisplayMemberOptions.IncludeConstantValue,
        delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
        extensionMethodStyle: SymbolDisplayExtensionMethodStyle.StaticMethod,
        parameterOptions: SymbolDisplayParameterOptions.IncludeExtensionThis | SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeType |
                          SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue,
        propertyStyle: SymbolDisplayPropertyStyle.NameOnly,
        kindOptions: SymbolDisplayKindOptions.IncludeMemberKeyword,
        miscellaneousOptions: Miscellaneous);

    static readonly SymbolDisplayFormat ParameterListFormat = MemberFormat.WithMemberOptions(SymbolDisplayMemberOptions.IncludeParameters);

    static readonly SymbolDisplayFormat TypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: Miscellaneous);

    static readonly SymbolDisplayFormat DeclaredNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
        miscellaneousOptions: Miscellaneous);

    static readonly SymbolDisplayFormat DisplayNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: Miscellaneous);

    readonly SymbolLinks Links;

    public Signatures(SymbolLinks links) => Links = links;

    /// <summary>Short display name such as "Query&lt;T&gt;", "operator +" or the type name for constructors.</summary>
    public static string DisplayName(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => constructor.ContainingType.Name,
        IPropertySymbol { IsIndexer: true } => "this[]",
        _ => symbol.ToDisplayString(DisplayNameFormat)
    };

    /// <summary>Tokens for a type reference such as a parameter or return type.</summary>
    public List<Token> Type(ITypeSymbol type)
    {
        var tokens = new TokenBuilder();
        tokens.AddParts(type.ToDisplayParts(TypeFormat), Links, self: null);
        return tokens.Items;
    }

    /// <summary>Full declaration of a member, including visible attributes and property accessors.</summary>
    public List<Token> Member(ISymbol member)
    {
        var tokens = new TokenBuilder();
        AddAttributes(tokens, member);

        if (member is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } enumValue)
        {
            tokens.Add("m", enumValue.Name);
            if (enumValue.HasConstantValue) { tokens.Add("o", " = "); tokens.Add("n", enumValue.ConstantValue?.ToString()); }
            return tokens.Items;
        }

        tokens.AddParts(member.ToDisplayParts(MemberFormat), Links, member);
        if (member is IPropertySymbol property) AddAccessors(tokens, property);
        return tokens.Items;
    }

    /// <summary>Full declaration of a type: attributes, modifiers, kind, name, base list and constraints.</summary>
    public List<Token> Declaration(INamedTypeSymbol type)
    {
        var tokens = new TokenBuilder();
        AddAttributes(tokens, type);

        tokens.Add("k", type.DeclaredAccessibility == Accessibility.Public ? "public" : "protected");
        foreach (var modifier in Modifiers(type)) { tokens.Add("w", " "); tokens.Add("k", modifier); }
        tokens.Add("w", " ");
        tokens.Add("k", KindKeyword(type));
        tokens.Add("w", " ");

        if (type.TypeKind == TypeKind.Delegate)
        {
            var invoke = type.DelegateInvokeMethod;
            tokens.AddParts(invoke.ReturnType.ToDisplayParts(TypeFormat), Links, self: null);
            tokens.Add("w", " ");
            tokens.AddParts(type.ToDisplayParts(DeclaredNameFormat), Links, type);
            tokens.AddParts(invoke.ToDisplayParts(ParameterListFormat).SkipWhile(part => part.Kind != SymbolDisplayPartKind.MethodName).Skip(1), Links, invoke);
            AddConstraints(tokens, type.TypeParameters);
            return tokens.Items;
        }

        tokens.AddParts(type.ToDisplayParts(DeclaredNameFormat), Links, type);

        var baseList = new List<ITypeSymbol>();
        if (type.TypeKind == TypeKind.Class && type.BaseType is { SpecialType: not SpecialType.System_Object }) baseList.Add(type.BaseType);
        baseList.AddRange(type.Interfaces);

        if (type.TypeKind == TypeKind.Enum && type.EnumUnderlyingType is { SpecialType: not SpecialType.System_Int32 } underlying) baseList.Add(underlying);

        for (int index = 0; index < baseList.Count; index++)
        {
            tokens.Add("o", index == 0 ? " : " : ", ");
            tokens.AddParts(baseList[index].ToDisplayParts(TypeFormat), Links, self: null);
        }

        AddConstraints(tokens, type.TypeParameters);
        return tokens.Items;
    }

    /// <summary>C# modifiers implied by a type declaration, in source order.</summary>
    public static List<string> Modifiers(INamedTypeSymbol type)
    {
        var modifiers = new List<string>();

        if (type.IsStatic) modifiers.Add("static");
        else if (type.TypeKind == TypeKind.Class && type.IsAbstract) modifiers.Add("abstract");
        else if (type.TypeKind == TypeKind.Class && type.IsSealed) modifiers.Add("sealed");

        if (type.TypeKind == TypeKind.Struct && type.IsReadOnly) modifiers.Add("readonly");
        if (type.TypeKind == TypeKind.Struct && type.IsRefLikeType) modifiers.Add("ref");
        return modifiers;
    }

    /// <summary>Keyword naming a type's kind, such as "class" or "record struct".</summary>
    public static string KindKeyword(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class => type.IsRecord ? "record" : "class",
        TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
        TypeKind.Interface => "interface",
        TypeKind.Enum => "enum",
        TypeKind.Delegate => "delegate",
        _ => type.TypeKind.ToString().ToLowerInvariant()
    };

    /// <summary>Attributes consumers can observe; compiler bookkeeping attributes are hidden.</summary>
    public static IEnumerable<AttributeData> VisibleAttributes(ISymbol symbol) => symbol.GetAttributes().Where(attribute =>
        attribute.AttributeClass is { } attributeClass &&
        attributeClass.ContainingNamespace?.ToDisplayString() != "System.Runtime.CompilerServices" &&
        attributeClass.ToDisplayString() != "System.Reflection.DefaultMemberAttribute");

    void AddAttributes(TokenBuilder tokens, ISymbol symbol)
    {
        foreach (var attribute in VisibleAttributes(symbol))
        {
            var attributeName = attribute.AttributeClass.ToDisplayString(TypeFormat);
            if (attributeName.EndsWith("Attribute", StringComparison.Ordinal) && attributeName.Length > "Attribute".Length) attributeName = attributeName[..^"Attribute".Length];

            tokens.Add("o", "[");
            tokens.Add("t", attributeName, Links.Ref(attribute.AttributeClass));

            var constructor = attribute.AttributeConstructor;
            var arguments = attribute.ConstructorArguments;

            if (arguments.Length > 0 || attribute.NamedArguments.Length > 0)
            {
                tokens.Add("o", "(");

                for (int index = 0; index < arguments.Length; index++)
                {
                    if (index > 0) tokens.Add("o", ", ");
                    bool expand = constructor != null && index < constructor.Parameters.Length && constructor.Parameters[index].IsParams && arguments[index].Kind == TypedConstantKind.Array;
                    AddConstant(tokens, arguments[index], expand);
                }

                for (int index = 0; index < attribute.NamedArguments.Length; index++)
                {
                    if (index > 0 || arguments.Length > 0) tokens.Add("o", ", ");
                    tokens.Add("m", attribute.NamedArguments[index].Key);
                    tokens.Add("o", " = ");
                    AddConstant(tokens, attribute.NamedArguments[index].Value, expand: false);
                }

                tokens.Add("o", ")");
            }

            tokens.Add("o", "]");
            tokens.Add("w", "\n");
        }
    }

    void AddConstant(TokenBuilder tokens, TypedConstant constant, bool expand)
    {
        switch (constant.Kind)
        {
            case TypedConstantKind.Array:
                if (!expand) tokens.Add("o", "[");
                for (int index = 0; index < constant.Values.Length; index++)
                {
                    if (index > 0) tokens.Add("o", ", ");
                    AddConstant(tokens, constant.Values[index], expand: false);
                }
                if (!expand) tokens.Add("o", "]");
                return;
            case TypedConstantKind.Type when constant.Value is ITypeSymbol typeValue:
                tokens.Add("k", "typeof");
                tokens.Add("o", "(");
                tokens.AddParts(typeValue.ToDisplayParts(TypeFormat), Links, self: null);
                tokens.Add("o", ")");
                return;
            case TypedConstantKind.Enum when constant.Type is INamedTypeSymbol enumType:
                var enumMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, constant.Value));
                tokens.AddParts(enumType.ToDisplayParts(TypeFormat), Links, self: null);
                tokens.Add("o", ".");
                tokens.Add("m", enumMember?.Name ?? constant.Value?.ToString(), enumMember != null ? Links.Ref(enumMember) : null);
                return;
            default:
                var text = constant.ToCSharpString();
                tokens.Add(constant.Value is string or char ? "s" : constant.Value is bool or null ? "k" : "n", text);
                return;
        }
    }

    void AddAccessors(TokenBuilder tokens, IPropertySymbol property)
    {
        tokens.Add("o", " {");
        AddAccessor(property.GetMethod, "get");
        AddAccessor(property.SetMethod, property.SetMethod is { IsInitOnly: true } ? "init" : "set");
        tokens.Add("o", " }");

        void AddAccessor(IMethodSymbol accessor, string keyword)
        {
            if (accessor == null || !SymbolLinks.IsVisible(accessor)) return;

            tokens.Add("w", " ");
            if (accessor.DeclaredAccessibility != property.DeclaredAccessibility) { tokens.Add("k", "protected"); tokens.Add("w", " "); }
            tokens.Add("k", keyword);
            tokens.Add("o", ";");
        }
    }

    void AddConstraints(TokenBuilder tokens, IEnumerable<ITypeParameterSymbol> typeParameters)
    {
        foreach (var typeParameter in typeParameters)
        {
            var constraints = new List<List<Token>>();

            if (typeParameter.HasUnmanagedTypeConstraint) constraints.Add([new Token("k", "unmanaged")]);
            else if (typeParameter.HasValueTypeConstraint) constraints.Add([new Token("k", "struct")]);
            else if (typeParameter.HasReferenceTypeConstraint) constraints.Add([new Token("k", "class")]);
            if (typeParameter.HasNotNullConstraint) constraints.Add([new Token("k", "notnull")]);
            constraints.AddRange(typeParameter.ConstraintTypes.Select(Type));
            if (typeParameter.HasConstructorConstraint) constraints.Add([new Token("k", "new"), new Token("o", "()")]);

            if (constraints.Count == 0) continue;

            tokens.Add("w", " ");
            tokens.Add("k", "where");
            tokens.Add("w", " ");
            tokens.Add("t", typeParameter.Name);
            tokens.Add("o", " : ");

            for (int index = 0; index < constraints.Count; index++)
            {
                if (index > 0) tokens.Add("o", ", ");
                foreach (var token in constraints[index]) tokens.Add(token.Kind, token.Text, token.Ref);
            }
        }
    }

    /// <summary>Accumulates tokens, merging adjacent unlinked fragments of the same kind.</summary>
    sealed class TokenBuilder
    {
        public List<Token> Items { get; } = [];

        public void Add(string kind, string text, string reference = null)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (reference == null && Items.Count > 0 && Items[^1] is { Ref: null } previous && previous.Kind == kind && kind is "o" or "w" or "x")
            {
                Items[^1] = previous with { Text = previous.Text + text };
                return;
            }

            Items.Add(new Token(kind, text, reference));
        }

        public void AddParts(IEnumerable<SymbolDisplayPart> parts, SymbolLinks links, ISymbol self)
        {
            foreach (var part in parts)
            {
                var kind = TokenKind(part.Kind);
                var symbol = part.Symbol;
                bool linkable = kind is "t" or "m" && symbol != null && !SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, self?.OriginalDefinition);
                Add(kind, part.ToString(), linkable ? links.Ref(symbol) : null);
            }
        }

        static string TokenKind(SymbolDisplayPartKind kind) => kind switch
        {
            SymbolDisplayPartKind.Keyword => "k",
            SymbolDisplayPartKind.ClassName or SymbolDisplayPartKind.StructName or SymbolDisplayPartKind.InterfaceName or SymbolDisplayPartKind.EnumName or
            SymbolDisplayPartKind.DelegateName or SymbolDisplayPartKind.RecordClassName or SymbolDisplayPartKind.RecordStructName or
            SymbolDisplayPartKind.ErrorTypeName or SymbolDisplayPartKind.TypeParameterName or SymbolDisplayPartKind.NamespaceName => "t",
            SymbolDisplayPartKind.MethodName or SymbolDisplayPartKind.ExtensionMethodName or SymbolDisplayPartKind.PropertyName or SymbolDisplayPartKind.FieldName or
            SymbolDisplayPartKind.EventName or SymbolDisplayPartKind.EnumMemberName or SymbolDisplayPartKind.ConstantName => "m",
            SymbolDisplayPartKind.ParameterName or SymbolDisplayPartKind.LocalName => "p",
            SymbolDisplayPartKind.NumericLiteral => "n",
            SymbolDisplayPartKind.StringLiteral => "s",
            SymbolDisplayPartKind.Punctuation or SymbolDisplayPartKind.Operator => "o",
            SymbolDisplayPartKind.Space or SymbolDisplayPartKind.LineBreak => "w",
            _ => "x"
        };
    }
}
