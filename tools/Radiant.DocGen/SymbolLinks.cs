using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;

namespace com.radiant.tools.docgen;

/// <summary>Assigns stable IDs and anchors to documented symbols and resolves links between them.</summary>
public sealed class SymbolLinks
{
    static readonly SymbolDisplayFormat AnchorTypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    readonly Dictionary<INamedTypeSymbol, string> TypeIds = new(SymbolEqualityComparer.Default);
    readonly Dictionary<ISymbol, string> Anchors = new(SymbolEqualityComparer.Default);
    readonly Dictionary<INamedTypeSymbol, List<ISymbol>> MembersByType = new(SymbolEqualityComparer.Default);

    public IReadOnlyList<INamedTypeSymbol> Types { get; }

    /// <summary>Collects the visible types of an assembly and assigns IDs and member anchors.</summary>
    public SymbolLinks(IAssemblySymbol assembly)
    {
        var types = new List<INamedTypeSymbol>();
        CollectTypes(assembly.GlobalNamespace, types);
        Types = types;

        foreach (var group in types.GroupBy(LocalId))
            foreach (var type in group)
                TypeIds[type] = group.Count() == 1 ? group.Key : type.ContainingNamespace.ToDisplayString() + "." + group.Key;

        foreach (var type in types)
        {
            var members = type.GetMembers().Where(IsDocumented).OrderBy(MemberOrder).ThenBy(member => member.Name, StringComparer.Ordinal).ThenBy(ParameterCount).ToList();
            MembersByType[type] = members;
            AssignAnchors(members);
        }
    }

    /// <summary>Whether a type is part of the documented surface.</summary>
    public bool IsDocumented(INamedTypeSymbol type) => TypeIds.ContainsKey(type.OriginalDefinition);

    public string TypeId(INamedTypeSymbol type) => TypeIds[type.OriginalDefinition];

    public string Anchor(ISymbol member) => Anchors[member];

    public IReadOnlyList<ISymbol> Members(INamedTypeSymbol type) => MembersByType[type];

    /// <summary>Returns a type ID, "TypeId#anchor", an external API URL, or null when the symbol has no page.</summary>
    public string Ref(ISymbol symbol)
    {
        switch (symbol)
        {
            case INamedTypeSymbol type:
                return TypeIds.TryGetValue(type.OriginalDefinition, out var typeId) ? typeId : ExternalUrl(type.OriginalDefinition);
            case IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol:
                var member = symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol.OriginalDefinition;
                if (Anchors.TryGetValue(member, out var anchor) && TypeIds.TryGetValue(member.ContainingType, out var ownerId)) return ownerId + "#" + anchor;
                return null;
            default:
                return null;
        }
    }

    /// <summary>Whether a symbol is reachable from outside the assembly.</summary>
    public static bool IsVisible(ISymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Accessibility.Public => true,
        Accessibility.Protected or Accessibility.ProtectedOrInternal => symbol.ContainingType is { IsSealed: false, IsValueType: false, IsStatic: false },
        _ => false
    };

    static void CollectTypes(INamespaceOrTypeSymbol container, List<INamedTypeSymbol> types)
    {
        if (container is INamespaceSymbol namespaceSymbol)
            foreach (var child in namespaceSymbol.GetNamespaceMembers()) CollectTypes(child, types);

        foreach (var type in container.GetTypeMembers().Where(IsVisible).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            types.Add(type);
            CollectTypes(type, types);
        }
    }

    static bool IsDocumented(ISymbol member) => !member.IsImplicitlyDeclared && IsVisible(member) && member switch
    {
        IMethodSymbol method => method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.UserDefinedOperator or MethodKind.Conversion,
        IPropertySymbol property => property.ExplicitInterfaceImplementations.IsEmpty,
        IEventSymbol eventSymbol => eventSymbol.ExplicitInterfaceImplementations.IsEmpty,
        IFieldSymbol => true,
        _ => false
    };

    static int MemberOrder(ISymbol member) => member switch
    {
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => 0,
        IMethodSymbol { MethodKind: MethodKind.Constructor } => 1,
        IFieldSymbol => 2,
        IPropertySymbol => 3,
        IEventSymbol => 4,
        IMethodSymbol { MethodKind: MethodKind.Ordinary } => 5,
        _ => 6
    };

    static int ParameterCount(ISymbol member) => member switch
    {
        IMethodSymbol method => method.Parameters.Length,
        IPropertySymbol property => property.Parameters.Length,
        _ => 0
    };

    static string LocalId(INamedTypeSymbol type)
    {
        var name = type.Arity > 0 ? $"{type.Name}-{type.Arity}" : type.Name;
        return type.ContainingType == null ? name : LocalId(type.ContainingType) + "." + name;
    }

    void AssignAnchors(List<ISymbol> members)
    {
        foreach (var group in members.GroupBy(AnchorBaseName))
        {
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (var member in group)
            {
                var anchor = group.Count() == 1 ? group.Key : group.Key + ParameterSuffix(member);
                var unique = anchor;
                for (int suffix = 2; !used.Add(unique); suffix++) unique = $"{anchor}-{suffix}";
                Anchors[member] = unique;
            }
        }
    }

    /// <summary>Anchor shared by every overload of a member; overloaded members also get a per-signature suffix.</summary>
    public static string AnchorBaseName(ISymbol member) => member switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } => "ctor",
        IPropertySymbol { IsIndexer: true } => "Item",
        _ => member.Name
    };

    static string ParameterSuffix(ISymbol member)
    {
        var parameters = member switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => []
        };

        var suffix = new StringBuilder();
        foreach (var parameter in parameters)
        {
            suffix.Append('-');
            if (parameter.RefKind != RefKind.None) suffix.Append(parameter.RefKind.ToString().ToLowerInvariant()).Append('-');
            suffix.Append(Slug(parameter.Type.ToDisplayString(AnchorTypeFormat)));
        }

        return suffix.ToString();
    }

    static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (var character in text.Replace("[]", "Array").Replace("*", "Ptr"))
            slug.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '-');

        return string.Join('-', slug.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    static string ExternalUrl(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Error || type.ContainingNamespace == null) return null;

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        var names = new List<string>();
        for (var current = type; current != null; current = current.ContainingType)
            names.Insert(0, current.Arity > 0 ? $"{current.Name}-{current.Arity}" : current.Name);

        var qualifiedName = namespaceName + "." + string.Join('.', names);

        if (namespaceName == "System" || namespaceName.StartsWith("System.", StringComparison.Ordinal))
            return "https://learn.microsoft.com/dotnet/api/" + qualifiedName.ToLowerInvariant();

        if (namespaceName.StartsWith("Microsoft.Xna.Framework", StringComparison.Ordinal))
            return "https://docs.monogame.net/api/" + qualifiedName + ".html";

        return null;
    }
}
