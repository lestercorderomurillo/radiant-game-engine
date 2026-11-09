using System.Collections.Generic;

namespace com.radiant.tools.docgen;

/// <summary>Documentation for one project: metadata, navigation groups and every public type.</summary>
public sealed class ApiModel
{
    public ApiMeta Meta { get; init; }
    public List<ApiGroup> Groups { get; init; } = [];
    public List<ApiType> Types { get; init; } = [];
}

/// <summary>Project and generation details shown in the site header.</summary>
public sealed class ApiMeta
{
    public string Name { get; init; }
    public string Version { get; init; }
    public string GeneratedAt { get; init; }
    public ApiRepository Repository { get; init; }
    public List<string> Namespaces { get; init; } = [];
}

/// <summary>Browsable repository location used to link declarations to source.</summary>
public sealed class ApiRepository
{
    public string Url { get; init; }
    public string Branch { get; init; }
    public string Commit { get; init; }
}

/// <summary>Top-level navigation section derived from the source tree, such as Core or Systems.</summary>
public sealed class ApiGroup
{
    public string Id { get; init; }
    public string Title { get; init; }
    public List<ApiArea> Areas { get; init; } = [];
}

/// <summary>Source folder within a group; a null title holds the group's own top-level types.</summary>
public sealed class ApiArea
{
    public string Id { get; init; }
    public string Title { get; init; }
    public List<string> Types { get; init; } = [];
}

/// <summary>A linkable label; Ref is a type ID, "TypeId#anchor", or an external URL.</summary>
public sealed class ApiLink
{
    public string Text { get; init; }
    public string Ref { get; init; }
}

/// <summary>A declaration location relative to the repository root.</summary>
public sealed class ApiSource
{
    public string Path { get; init; }
    public int Line { get; init; }
}

/// <summary>A named documentation entry such as a type parameter.</summary>
public sealed class ApiNamedDoc
{
    public string Name { get; init; }
    public string Description { get; init; }
}

/// <summary>A method or delegate parameter with its type and documentation.</summary>
public sealed class ApiParameter
{
    public string Name { get; init; }
    public List<Token> Type { get; init; } = [];
    public string Modifier { get; init; }
    public string DefaultValue { get; init; }
    public string Description { get; init; }
}

/// <summary>An exception a member documents as thrown.</summary>
public sealed class ApiException
{
    public ApiLink Type { get; init; }
    public string Description { get; init; }
}

/// <summary>Documentation shared by types and members, already converted to Markdown.</summary>
public abstract class ApiDocumented
{
    public string Summary { get; set; }
    public string SummaryText { get; set; }
    public string Remarks { get; set; }
    public List<string> Examples { get; set; } = [];
    public List<ApiNamedDoc> TypeParameters { get; set; } = [];
    public string Obsolete { get; set; }
}

/// <summary>A public class, struct, interface, enum or delegate.</summary>
public sealed class ApiType : ApiDocumented
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string FullName { get; init; }
    public string Namespace { get; init; }
    public string Kind { get; init; }
    public string Area { get; init; }
    public List<string> Modifiers { get; init; } = [];
    public List<Token> Declaration { get; init; } = [];
    public List<ApiLink> BaseTypes { get; init; } = [];
    public List<ApiLink> Interfaces { get; init; } = [];
    public List<ApiLink> Derived { get; init; } = [];
    public List<ApiLink> Implementations { get; init; } = [];
    public List<ApiLink> Nested { get; init; } = [];
    public ApiLink ContainingType { get; init; }
    public List<ApiSource> Sources { get; init; } = [];
    public List<ApiMember> Members { get; init; } = [];
    public List<ApiInheritedGroup> Inherited { get; init; } = [];
    public List<ApiLink> Extensions { get; init; } = [];
    public List<ApiParameter> Parameters { get; set; } = [];
    public string Returns { get; set; }
}

/// <summary>A constructor, field, property, event, method, operator or enum value; overloads share a Group anchor.</summary>
public sealed class ApiMember : ApiDocumented
{
    public string Id { get; init; }
    public string Group { get; init; }
    public string Name { get; init; }
    public string Kind { get; init; }
    public List<string> Modifiers { get; init; } = [];
    public List<Token> Signature { get; init; } = [];
    public List<ApiParameter> Parameters { get; init; } = [];
    public List<Token> ReturnType { get; init; } = [];
    public string Returns { get; set; }
    public string Value { get; set; }
    public string ConstantValue { get; init; }
    public List<ApiException> Exceptions { get; set; } = [];
    public ApiLink Overrides { get; init; }
    public List<ApiLink> Implements { get; init; } = [];
    public ApiSource Source { get; init; }
}

/// <summary>Members a type inherits from one documented base type.</summary>
public sealed class ApiInheritedGroup
{
    public ApiLink From { get; init; }
    public List<ApiLink> Members { get; init; } = [];
}

/// <summary>One highlighted fragment of a declaration: k keyword, t type, m member, p parameter, n number, s string, o punctuation, w whitespace, x text.</summary>
public readonly record struct Token(string Kind, string Text, string Ref = null);
