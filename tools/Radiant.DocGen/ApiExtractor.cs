using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace com.radiant.tools.docgen;

/// <summary>Walks a compilation's public surface and builds the documentation model.</summary>
public sealed class ApiExtractor
{
    static readonly string[] GroupOrder = ["Core", "Components", "Systems", "Extensions", "Editor", "Runtime", "Multiplayer"];
    static readonly Dictionary<string, string> SegmentTitles = new() { ["mplay"] = "Multiplayer" };
    static readonly HashSet<string> TransparentSegments = ["bundle"];

    static readonly SymbolDisplayFormat TypeNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    readonly ProjectSources Project;
    readonly SymbolLinks Links;
    readonly Signatures Signatures;
    readonly DocComments Comments;
    readonly Dictionary<INamedTypeSymbol, (string Group, string Area)> Areas = new(SymbolEqualityComparer.Default);

    /// <summary>Prepares symbol links, signatures and comment conversion for a compilation.</summary>
    public ApiExtractor(Compilation compilation, ProjectSources project)
    {
        Project = project;
        Links = new SymbolLinks(compilation.Assembly);
        Signatures = new Signatures(Links);
        Comments = new DocComments(compilation, Links);
    }

    /// <summary>Builds every documented type, its navigation area and the cross-type relationships.</summary>
    public ApiModel Extract()
    {
        AssignAreas();

        var derived = Links.Types.ToDictionary(type => type, _ => new List<ApiLink>(), SymbolEqualityComparer.Default);
        var implementations = Links.Types.ToDictionary(type => type, _ => new List<ApiLink>(), SymbolEqualityComparer.Default);
        var extensions = Links.Types.ToDictionary(type => type, _ => new List<ApiLink>(), SymbolEqualityComparer.Default);

        foreach (var type in Links.Types)
        {
            if (type.BaseType?.OriginalDefinition is { } baseType && Links.IsDocumented(baseType)) derived[baseType].Add(TypeLink(type));

            foreach (var implemented in type.Interfaces.Select(implementedInterface => implementedInterface.OriginalDefinition).Where(Links.IsDocumented))
                implementations[implemented].Add(TypeLink(type));

            foreach (var method in Links.Members(type).OfType<IMethodSymbol>().Where(method => method.IsExtensionMethod))
                if (method.Parameters[0].Type.OriginalDefinition is INamedTypeSymbol target && Links.IsDocumented(target))
                    extensions[target].Add(new ApiLink { Text = type.Name + "." + method.Name, Ref = Links.Ref(method) });
        }

        var types = Links.Types.Select(type => BuildType(type, derived[type], implementations[type], extensions[type])).ToList();

        return new ApiModel
        {
            Meta = new ApiMeta
            {
                Name = Project.Name,
                Version = Project.Version,
                GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
                Repository = Project.Repository,
                Namespaces = types.Select(type => type.Namespace).Distinct().Order(StringComparer.Ordinal).ToList()
            },
            Groups = BuildGroups(types),
            Types = types
        };
    }

    void AssignAreas()
    {
        var directories = Links.Types.Select(type => (Type: type, Directory: SourceDirectory(type))).ToList();
        var sourceRoot = CommonDirectory(directories.Select(entry => entry.Directory));

        foreach (var (type, directory) in directories)
        {
            var segments = directory[sourceRoot.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => !TransparentSegments.Contains(segment)).ToList();
            Areas[type] = segments.Count == 0 ? ("General", null) : (Title(segments[0]), segments.Count > 1 ? Title(segments[1]) : null);
        }
    }

    string SourceDirectory(INamedTypeSymbol type)
    {
        var path = PrimaryLocation(type)?.SourceTree.FilePath;
        return path == null ? "" : Path.GetDirectoryName(Project.RelativePath(path)).Replace('\\', '/');
    }

    static string CommonDirectory(IEnumerable<string> directories)
    {
        string[] common = null;

        foreach (var segments in directories.Select(directory => directory.Split('/', StringSplitOptions.RemoveEmptyEntries)))
            common = common == null ? segments : common.Zip(segments).TakeWhile(pair => pair.First == pair.Second).Select(pair => pair.First).ToArray();

        return common == null || common.Length == 0 ? "" : string.Join('/', common);
    }

    static string Title(string segment) => SegmentTitles.TryGetValue(segment, out var title) ? title : char.ToUpperInvariant(segment[0]) + segment[1..];

    static string Slug(string title) => title.ToLowerInvariant().Replace(' ', '-');

    string AreaId(INamedTypeSymbol type)
    {
        var (group, area) = Areas[type];
        return area == null ? Slug(group) : Slug(group) + "/" + Slug(area);
    }

    List<ApiGroup> BuildGroups(List<ApiType> types)
    {
        var typesById = types.ToDictionary(type => type.Id);
        var areas = Links.Types.GroupBy(type => Areas[type], type => Links.TypeId(type));

        return areas
            .GroupBy(area => area.Key.Group)
            .OrderBy(group => Array.IndexOf(GroupOrder, group.Key) is var order and >= 0 ? order : GroupOrder.Length)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ApiGroup
            {
                Id = Slug(group.Key),
                Title = group.Key,
                Areas = group
                    .OrderBy(area => area.Key.Area != null)
                    .ThenBy(area => area.Key.Area, StringComparer.OrdinalIgnoreCase)
                    .Select(area => new ApiArea
                    {
                        Id = area.Key.Area == null ? Slug(group.Key) : Slug(group.Key) + "/" + Slug(area.Key.Area),
                        Title = area.Key.Area,
                        Types = area.OrderBy(id => typesById[id].Name, StringComparer.OrdinalIgnoreCase).ToList()
                    })
                    .ToList()
            })
            .ToList();
    }

    ApiType BuildType(INamedTypeSymbol type, List<ApiLink> derived, List<ApiLink> implementations, List<ApiLink> extensions)
    {
        var baseTypes = new List<ApiLink>();
        for (var current = type.TypeKind == TypeKind.Class ? type.BaseType : null; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
            baseTypes.Add(TypeLink(current));

        var apiType = new ApiType
        {
            Id = Links.TypeId(type),
            Name = type.ToDisplayString(TypeNameFormat),
            FullName = type.ToDisplayString(),
            Namespace = type.ContainingNamespace.ToDisplayString(),
            Kind = type.TypeKind.ToString().ToLowerInvariant(),
            Area = AreaId(type),
            Modifiers = Signatures.Modifiers(type),
            Declaration = Signatures.Declaration(type),
            BaseTypes = baseTypes,
            Interfaces = type.TypeKind is TypeKind.Enum or TypeKind.Delegate ? [] : type.AllInterfaces.Select(TypeLink).ToList(),
            Derived = derived.OrderBy(link => link.Text, StringComparer.Ordinal).ToList(),
            Implementations = implementations.OrderBy(link => link.Text, StringComparer.Ordinal).ToList(),
            Nested = type.GetTypeMembers().Where(Links.IsDocumented).Select(TypeLink).ToList(),
            ContainingType = type.ContainingType != null ? TypeLink(type.ContainingType) : null,
            Sources = TypeSources(type),
            Members = Links.Members(type).Select(BuildMember).ToList(),
            Inherited = InheritedGroups(type),
            Extensions = extensions,
            Parameters = type.DelegateInvokeMethod is { } invoke ? Parameters(invoke.Parameters, Comments.Parameters(type)) : []
        };

        Comments.Apply(type, apiType);
        apiType.Obsolete = ObsoleteMessage(type);
        return apiType;
    }

    ApiMember BuildMember(ISymbol member)
    {
        var parameters = member switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => []
        };

        var apiMember = new ApiMember
        {
            Id = Links.Anchor(member),
            Group = SymbolLinks.AnchorBaseName(member),
            Name = Signatures.DisplayName(member),
            Kind = MemberKind(member),
            Modifiers = MemberModifiers(member),
            Signature = Signatures.Member(member),
            Parameters = Parameters(parameters, Comments.Parameters(member)),
            ReturnType = member is IMethodSymbol { ReturnsVoid: false, MethodKind: not MethodKind.Constructor } returning ? Signatures.Type(returning.ReturnType) : [],
            ConstantValue = member is IFieldSymbol { HasConstantValue: true } constant ? FormatValue(constant.Type, constant.ConstantValue) : null,
            Overrides = OverriddenMember(member) is { } overridden ? MemberLink(overridden) : null,
            Implements = member.ContainingType.TypeKind == TypeKind.Interface ? [] : ImplementedMembers(member).Select(MemberLink).ToList(),
            Source = Source(member.Locations.FirstOrDefault(location => location.IsInSource))
        };

        Comments.Apply(member, apiMember);
        apiMember.Obsolete = ObsoleteMessage(member);
        return apiMember;
    }

    List<ApiParameter> Parameters(IEnumerable<IParameterSymbol> parameters, Dictionary<string, string> descriptions) => parameters.Select(parameter => new ApiParameter
    {
        Name = parameter.Name,
        Type = Signatures.Type(parameter.Type),
        Modifier = ParameterModifier(parameter),
        DefaultValue = parameter.HasExplicitDefaultValue ? FormatValue(parameter.Type, parameter.ExplicitDefaultValue) : null,
        Description = descriptions.GetValueOrDefault(parameter.Name)
    }).ToList();

    static string ParameterModifier(IParameterSymbol parameter)
    {
        if (parameter.IsParams) return "params";
        if (parameter.IsThis) return "this";

        return parameter.RefKind switch
        {
            RefKind.Ref => "ref",
            RefKind.Out => "out",
            RefKind.In => "in",
            RefKind.RefReadOnlyParameter => "ref readonly",
            _ => null
        };
    }

    static string FormatValue(ITypeSymbol type, object value)
    {
        if (type.TypeKind == TypeKind.Enum && value != null)
        {
            var member = type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, value));
            if (member != null) return type.Name + "." + member.Name;
        }

        if (value == null) return type.IsValueType ? "default" : "null";
        return SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
    }

    static string MemberKind(ISymbol member) => member switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
        IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } => "operator",
        IMethodSymbol => "method",
        IPropertySymbol { IsIndexer: true } => "indexer",
        IPropertySymbol => "property",
        IEventSymbol => "event",
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => "value",
        IFieldSymbol { IsConst: true } => "constant",
        _ => "field"
    };

    static List<string> MemberModifiers(ISymbol member)
    {
        var modifiers = new List<string>();
        if (member.ContainingType.TypeKind == TypeKind.Enum) return modifiers;

        bool inInterface = member.ContainingType.TypeKind == TypeKind.Interface;

        if (member.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal) modifiers.Add("protected");
        if (member is IFieldSymbol { IsConst: true }) modifiers.Add("const");
        else if (member.IsStatic) modifiers.Add("static");
        if (member.IsAbstract && !inInterface) modifiers.Add("abstract");
        if (member.IsVirtual) modifiers.Add("virtual");
        if (member.IsOverride) modifiers.Add(member.IsSealed ? "sealed override" : "override");
        if (member is IFieldSymbol { IsReadOnly: true }) modifiers.Add("readonly");
        if (member is IMethodSymbol { IsExtensionMethod: true }) modifiers.Add("extension");
        if (member is IMethodSymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRef: true }) modifiers.Add("ref");
        return modifiers;
    }

    static ISymbol OverriddenMember(ISymbol member) => member switch
    {
        IMethodSymbol method => method.OverriddenMethod,
        IPropertySymbol property => property.OverriddenProperty,
        IEventSymbol eventSymbol => eventSymbol.OverriddenEvent,
        _ => null
    };

    static IEnumerable<ISymbol> ImplementedMembers(ISymbol member) => member.ContainingType.AllInterfaces
        .SelectMany(implementedInterface => implementedInterface.GetMembers(member.Name))
        .Where(candidate => SymbolEqualityComparer.Default.Equals(member.ContainingType.FindImplementationForInterfaceMember(candidate), member));

    List<ApiInheritedGroup> InheritedGroups(INamedTypeSymbol type)
    {
        var groups = new List<ApiInheritedGroup>();
        var seen = Links.Members(type).Select(member => member.Name).ToHashSet(StringComparer.Ordinal);

        for (var current = type.BaseType?.OriginalDefinition; current != null; current = current.BaseType?.OriginalDefinition)
        {
            if (!Links.IsDocumented(current)) break;

            var inherited = Links.Members(current)
                .Where(member => member is not IMethodSymbol { MethodKind: MethodKind.Constructor } && !member.IsStatic && seen.Add(member.Name))
                .Select(member => new ApiLink { Text = Signatures.DisplayName(member), Ref = Links.TypeId(current) + "#" + SymbolLinks.AnchorBaseName(member) })
                .ToList();

            if (inherited.Count > 0) groups.Add(new ApiInheritedGroup { From = TypeLink(current), Members = inherited });
        }

        return groups;
    }

    ApiLink TypeLink(INamedTypeSymbol type) => new() { Text = type.ToDisplayString(TypeNameFormat), Ref = Links.Ref(type) };

    ApiLink MemberLink(ISymbol member) => new()
    {
        Text = member.ContainingType.ToDisplayString(TypeNameFormat) + "." + Signatures.DisplayName(member),
        Ref = Links.Ref(member) ?? Links.Ref(member.ContainingType)
    };

    static Location PrimaryLocation(INamedTypeSymbol type)
    {
        var locations = type.Locations.Where(location => location.IsInSource).ToList();
        var outermost = type;
        while (outermost.ContainingType != null) outermost = outermost.ContainingType;

        return locations.FirstOrDefault(location => Path.GetFileNameWithoutExtension(location.SourceTree.FilePath) == outermost.Name) ?? locations.FirstOrDefault();
    }

    List<ApiSource> TypeSources(INamedTypeSymbol type)
    {
        var primary = PrimaryLocation(type);
        return type.Locations.Where(location => location.IsInSource).OrderBy(location => location == primary ? 0 : 1).Select(Source).ToList();
    }

    ApiSource Source(Location location)
    {
        if (location == null) return null;

        var span = location.GetLineSpan();
        return new ApiSource { Path = Project.RelativePath(span.Path), Line = span.StartLinePosition.Line + 1 };
    }

    static string ObsoleteMessage(ISymbol symbol)
    {
        var obsolete = symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute");
        if (obsolete == null) return null;

        return obsolete.ConstructorArguments.FirstOrDefault().Value as string ?? "Obsolete";
    }
}
