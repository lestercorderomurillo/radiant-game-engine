using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;

namespace com.radiant.tools.docgen;

/// <summary>Reads XML documentation comments, resolves inheritdoc, and converts them to Markdown.</summary>
public sealed partial class DocComments
{
    const int MaxInheritanceDepth = 8;

    readonly Compilation Compilation;
    readonly SymbolLinks Links;
    readonly Dictionary<ISymbol, XElement> ResolvedComments = new(SymbolEqualityComparer.Default);

    public DocComments(Compilation compilation, SymbolLinks links)
    {
        Compilation = compilation;
        Links = links;
    }

    /// <summary>Copies a symbol's resolved documentation onto a documented model entry.</summary>
    public void Apply(ISymbol symbol, ApiDocumented target)
    {
        var comment = Resolved(symbol);
        if (comment == null) return;

        var summary = comment.Element("summary");
        target.Summary = Markdown(summary);
        target.SummaryText = PlainText(summary);
        target.Remarks = Markdown(comment.Element("remarks"));
        target.Examples = comment.Descendants("example").Select(Markdown).Where(example => example != null).ToList();
        target.TypeParameters = NamedEntries(comment, "typeparam");

        if (target is ApiMember member)
        {
            member.Returns = Markdown(comment.Element("returns"));
            member.Value = Markdown(comment.Element("value"));
            member.Exceptions = comment.Elements("exception").Select(exception => new ApiException
            {
                Type = CrefLink(exception.Attribute("cref")?.Value),
                Description = Markdown(exception)
            }).ToList();
        }

        if (target is ApiType type) type.Returns = Markdown(comment.Element("returns"));
    }

    /// <summary>Markdown descriptions of a member's documented parameters, keyed by name.</summary>
    public Dictionary<string, string> Parameters(ISymbol symbol)
    {
        var comment = Resolved(symbol);
        if (comment == null) return new();

        return NamedEntries(comment, "param").Where(entry => entry.Description != null).GroupBy(entry => entry.Name).ToDictionary(group => group.Key, group => group.First().Description);
    }

    XElement Resolved(ISymbol symbol)
    {
        if (!ResolvedComments.TryGetValue(symbol, out var comment)) ResolvedComments[symbol] = comment = Resolve(symbol, depth: 0);
        return comment;
    }

    XElement Resolve(ISymbol symbol, int depth)
    {
        var comment = Parse(symbol);
        bool inherit = comment == null ? IsInheritor(symbol) : comment.Element("inheritdoc") != null;
        if (!inherit || depth >= MaxInheritanceDepth) return comment;

        var cref = comment?.Element("inheritdoc")?.Attribute("cref")?.Value;
        var source = cref != null ? DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, Compilation) : InheritanceSource(symbol);
        if (source == null) return comment;

        var inherited = Resolve(source, depth + 1);
        if (inherited == null) return comment;
        if (comment == null) return inherited;

        var merged = new XElement("member", comment.Elements().Where(element => element.Name != "inheritdoc"));
        var present = merged.Elements().Select(EntryKey).ToHashSet();
        merged.Add(inherited.Elements().Where(element => !present.Contains(EntryKey(element))));
        return merged;
    }

    static XElement Parse(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true);
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            return root.HasElements ? root : null;
        }
        catch (XmlException exception)
        {
            Console.Error.WriteLine($"docgen: warning: malformed XML documentation on {symbol.ToDisplayString()}: {exception.Message}");
            return null;
        }
    }

    static string EntryKey(XElement element) => element.Name.LocalName + ":" + element.Attribute("name")?.Value;

    static bool IsInheritor(ISymbol symbol) => symbol.IsOverride || InterfaceSource(symbol) != null;

    static ISymbol InheritanceSource(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol type => type.BaseType is { SpecialType: SpecialType.None } baseType ? baseType : type.Interfaces.FirstOrDefault(),
        IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => constructor.ContainingType.BaseType?.InstanceConstructors
            .FirstOrDefault(candidate => candidate.Parameters.Select(parameter => parameter.Type).SequenceEqual(constructor.Parameters.Select(parameter => parameter.Type), SymbolEqualityComparer.Default)),
        IMethodSymbol { OverriddenMethod: { } overridden } => overridden,
        IPropertySymbol { OverriddenProperty: { } overridden } => overridden,
        IEventSymbol { OverriddenEvent: { } overridden } => overridden,
        _ => InterfaceSource(symbol)
    };

    static ISymbol InterfaceSource(ISymbol symbol)
    {
        if (symbol.ContainingType == null || symbol is INamedTypeSymbol) return null;

        return symbol.ContainingType.AllInterfaces
            .SelectMany(implementedInterface => implementedInterface.GetMembers(symbol.Name))
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(candidate), symbol));
    }

    List<ApiNamedDoc> NamedEntries(XElement comment, string tag) => comment.Elements(tag)
        .Select(entry => new ApiNamedDoc { Name = entry.Attribute("name")?.Value, Description = Markdown(entry) })
        .Where(entry => entry.Name != null)
        .ToList();

    /// <summary>Links a cref ID to its documented page, falling back to the referenced name.</summary>
    ApiLink CrefLink(string cref)
    {
        if (cref == null) return null;

        var symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, Compilation);
        return new ApiLink { Text = symbol != null ? CrefName(symbol) : CrefFallbackName(cref), Ref = symbol != null ? Links.Ref(symbol) : null };
    }

    static string CrefName(ISymbol symbol) => symbol is INamedTypeSymbol || symbol.ContainingType == null
        ? Signatures.DisplayName(symbol)
        : symbol.ContainingType.Name + "." + Signatures.DisplayName(symbol);

    static string CrefFallbackName(string cref)
    {
        var name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var parameterStart = name.IndexOf('(');
        if (parameterStart >= 0) name = name[..parameterStart];
        return name[(name.LastIndexOf('.') + 1)..];
    }

    string Markdown(XElement element)
    {
        if (element == null) return null;

        var builder = new StringBuilder();
        AppendNodes(builder, element, plain: false);
        var markdown = NormalizeMarkdown(builder.ToString());
        return markdown.Length > 0 ? markdown : null;
    }

    string PlainText(XElement element)
    {
        if (element == null) return null;

        var builder = new StringBuilder();
        AppendNodes(builder, element, plain: true);
        var text = Whitespace().Replace(builder.ToString(), " ").Trim();
        return text.Length > 0 ? text : null;
    }

    void AppendNodes(StringBuilder builder, XElement element, bool plain)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text) builder.Append(plain ? Collapse(text.Value) : Paragraphs(text.Value));
            else if (node is XElement child) AppendElement(builder, child, plain);
        }
    }

    void AppendElement(StringBuilder builder, XElement element, bool plain)
    {
        switch (element.Name.LocalName)
        {
            case "c":
                builder.Append(plain ? element.Value : InlineCode(element.Value));
                break;
            case "code":
                if (!plain) builder.Append("\n\n```csharp\n").Append(Dedent(element.Value)).Append("\n```\n\n");
                break;
            case "para":
                builder.Append(plain ? " " : "\n\n");
                AppendNodes(builder, element, plain);
                builder.Append(plain ? " " : "\n\n");
                break;
            case "example":
                // Examples nested inside other tags are hoisted into ApiDocumented.Examples.
                break;
            case "br":
                builder.Append(plain ? " " : "  \n");
                break;
            case "see" or "seealso":
                AppendReference(builder, element, plain);
                break;
            case "paramref" or "typeparamref":
                var name = element.Attribute("name")?.Value ?? "";
                builder.Append(plain ? name : InlineCode(name));
                break;
            case "list":
                AppendList(builder, element, plain);
                break;
            case "b" or "strong" when !plain:
                builder.Append("**");
                AppendNodes(builder, element, plain);
                builder.Append("**");
                break;
            case "i" or "em" when !plain:
                builder.Append('*');
                AppendNodes(builder, element, plain);
                builder.Append('*');
                break;
            default:
                AppendNodes(builder, element, plain);
                break;
        }
    }

    void AppendReference(StringBuilder builder, XElement element, bool plain)
    {
        var label = Collapse(element.Value).Trim();

        if (element.Attribute("langword")?.Value is { } langword) { builder.Append(plain ? langword : InlineCode(langword)); return; }

        if (element.Attribute("href")?.Value is { } href)
        {
            var text = label.Length > 0 ? label : href;
            builder.Append(plain ? text : $"[{Escape(text)}](<{href}>)");
            return;
        }

        var link = CrefLink(element.Attribute("cref")?.Value);
        if (link == null) return;

        var display = label.Length > 0 ? label : link.Text;
        if (plain) builder.Append(display);
        else if (link.Ref == null) builder.Append(InlineCode(display));
        else builder.Append($"[{InlineCode(display)}](<{Href(link.Ref)}>)");
    }

    void AppendList(StringBuilder builder, XElement list, bool plain)
    {
        bool numbered = list.Attribute("type")?.Value == "number";
        int index = 1;

        builder.Append(plain ? " " : "\n\n");

        foreach (var item in list.Elements("item"))
        {
            var term = item.Element("term");
            var description = item.Element("description");

            if (!plain) builder.Append(numbered ? $"{index++}. " : "- ");

            if (term != null)
            {
                if (!plain) builder.Append("**");
                AppendNodes(builder, term, plain);
                builder.Append(plain ? ": " : "** — ");
            }

            AppendNodes(builder, description ?? item, plain);
            builder.Append(plain ? "; " : "\n");
        }

        builder.Append(plain ? " " : "\n");
    }

    /// <summary>Links inside Markdown use the xref: scheme for in-project pages so the site can route them.</summary>
    static string Href(string reference) => reference.StartsWith("http", StringComparison.Ordinal) ? reference : "xref:" + reference;

    static string InlineCode(string code)
    {
        var fence = code.Contains('`') ? "``" : "`";
        return $"{fence}{code.Trim()}{fence}";
    }

    static string Collapse(string text) => Whitespace().Replace(text, " ");

    /// <summary>Escapes text and keeps blank-line paragraph breaks authors wrote inside a tag.</summary>
    static string Paragraphs(string text) => string.Join("\n\n", BlankLine().Split(text).Select(paragraph => Escape(Collapse(paragraph))));

    static string Escape(string text) => MarkdownSpecial().Replace(text, @"\$0");

    static string Dedent(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);

        int indent = lines.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(line => line.Length >= indent ? line[indent..].TrimEnd() : line.TrimEnd()));
    }

    static string NormalizeMarkdown(string markdown)
    {
        var lines = new List<string>();
        bool inFence = false;

        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;

            if (inFence || line.StartsWith("```", StringComparison.Ordinal)) { lines.Add(line); continue; }

            var trimmed = line.Trim();
            if (trimmed.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0)) continue;
            lines.Add(line.EndsWith("  ", StringComparison.Ordinal) && trimmed.Length > 0 ? trimmed + "  " : trimmed);
        }

        return string.Join('\n', lines).Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"[\\`*_\[\]<>]")]
    private static partial Regex MarkdownSpecial();
}
