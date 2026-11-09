using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace com.radiant.tools.docgen;

/// <summary>Writes the site's navigation index, search index and one JSON file per type.</summary>
public static class ApiWriter
{
    const int SearchSummaryLength = 160;

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmptyCollections } },
        Converters = { new TokenConverter() }
    };

    /// <summary>Writes all files, then removes type files left over from a previous run.</summary>
    public static void Write(ApiModel model, string outputDirectory)
    {
        var typesDirectory = Path.Combine(outputDirectory, "types");
        Directory.CreateDirectory(typesDirectory);

        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in model.Types)
        {
            var path = Path.Combine(typesDirectory, type.Id + ".json");
            WriteJson(path, type);
            written.Add(Path.GetFullPath(path));
        }

        foreach (var stale in Directory.EnumerateFiles(typesDirectory, "*.json").Where(path => !written.Contains(Path.GetFullPath(path))))
            File.Delete(stale);

        WriteJson(Path.Combine(outputDirectory, "search.json"), SearchEntries(model));
        WriteJson(Path.Combine(outputDirectory, "index.json"), new
        {
            model.Meta,
            model.Groups,
            Types = model.Types.Select(type => new
            {
                type.Id,
                type.Name,
                type.Kind,
                type.Namespace,
                type.Area,
                Summary = type.SummaryText,
                Members = type.Members.Count,
                Source = type.Sources.FirstOrDefault()?.Path
            })
        });
    }

    /// <summary>One entry per member group: t type ID, a anchor, n name, k kind, s summary.</summary>
    static IEnumerable<object> SearchEntries(ApiModel model) => model.Types.SelectMany(type => type.Members
        .GroupBy(member => member.Group)
        .Select(group => group.FirstOrDefault(member => member.SummaryText != null) ?? group.First())
        .Select(member => new { T = type.Id, A = member.Group, N = member.Name, K = member.Kind, S = Truncate(member.SummaryText) }));

    static string Truncate(string text) => text == null || text.Length <= SearchSummaryLength ? text : text[..(SearchSummaryLength - 1)].TrimEnd() + "…";

    static void WriteJson(string path, object value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, path, overwrite: true);
    }

    static void SkipEmptyCollections(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
            if (property.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                property.ShouldSerialize = (_, value) => value is not ICollection collection || collection.Count > 0;
    }

    /// <summary>Serializes tokens as compact [kind, text, ref?] arrays.</summary>
    sealed class TokenConverter : JsonConverter<Token>
    {
        public override Token Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Token token, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(token.Kind);
            writer.WriteStringValue(token.Text);
            if (token.Ref != null) writer.WriteStringValue(token.Ref);
            writer.WriteEndArray();
        }
    }
}
