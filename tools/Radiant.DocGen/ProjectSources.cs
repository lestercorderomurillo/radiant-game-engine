using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

using Microsoft.Extensions.FileSystemGlobbing;

namespace com.radiant.tools.docgen;

/// <summary>Source files, package assemblies and repository metadata read from a project file and its project references.</summary>
public sealed class ProjectSources
{
    public string ProjectDirectory { get; private init; }
    public string Name { get; private init; }
    public string Version { get; private init; }
    public bool AllowUnsafe { get; private init; }
    public IReadOnlyList<string> SourceFiles { get; private init; }
    public IReadOnlyList<string> PackageAssemblies { get; private init; }
    public ApiRepository Repository { get; private init; }

    /// <summary>Reads Compile items and package compile assets for a project and, recursively, its project references.</summary>
    public static ProjectSources Load(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        var properties = XDocument.Load(projectPath).Root.Elements("PropertyGroup").Elements().ToLookup(element => element.Name.LocalName, element => element.Value.Trim());
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var sourceFiles = new SortedSet<string>(StringComparer.Ordinal);
        var packageAssemblies = new SortedSet<string>(StringComparer.Ordinal);
        bool allowUnsafe = false;

        void Visit(string path)
        {
            if (!visited.Add(path)) return;

            var project = XDocument.Load(path).Root;
            var directory = Path.GetDirectoryName(path);

            sourceFiles.UnionWith(ExpandCompileItems(project, directory));
            packageAssemblies.UnionWith(ReadPackageAssemblies(Path.Combine(directory, "obj", "project.assets.json")));
            allowUnsafe |= project.Elements("PropertyGroup").Elements("AllowUnsafeBlocks").Any(element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

            foreach (var reference in project.Elements("ItemGroup").Elements("ProjectReference").Select(element => element.Attribute("Include")?.Value).Where(include => include != null))
                Visit(Path.GetFullPath(Path.Combine(directory, reference.Replace('\\', '/'))));
        }

        Visit(projectPath);

        if (sourceFiles.Count == 0) throw new InvalidOperationException($"No <Compile Include> items found in {projectPath} or its project references.");

        return new ProjectSources
        {
            ProjectDirectory = projectDirectory,
            Name = properties["AssemblyName"].LastOrDefault() ?? Path.GetFileNameWithoutExtension(projectPath),
            Version = properties["Version"].LastOrDefault() ?? "0.0.0",
            AllowUnsafe = allowUnsafe,
            SourceFiles = [.. sourceFiles],
            PackageAssemblies = [.. packageAssemblies],
            Repository = ReadRepository(projectDirectory)
        };
    }

    /// <summary>Returns a forward-slash path relative to the project directory.</summary>
    public string RelativePath(string path) => Path.GetRelativePath(ProjectDirectory, path).Replace('\\', '/');

    static List<string> ExpandCompileItems(XElement project, string projectDirectory)
    {
        var files = new List<string>();

        foreach (var compile in project.Elements("ItemGroup").Elements("Compile"))
        {
            var include = compile.Attribute("Include")?.Value;
            if (include == null) continue;

            var matcher = new Matcher(StringComparison.Ordinal);
            foreach (var pattern in SplitPatterns(include)) matcher.AddInclude(pattern);
            foreach (var pattern in SplitPatterns(compile.Attribute("Exclude")?.Value)) matcher.AddExclude(pattern);

            foreach (var file in matcher.GetResultsInFullPath(projectDirectory)) files.Add(Path.GetFullPath(file));
        }

        return files;
    }

    static IEnumerable<string> SplitPatterns(string patterns) =>
        (patterns ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(pattern => pattern.Replace('\\', '/'));

    static List<string> ReadPackageAssemblies(string assetsPath)
    {
        if (!File.Exists(assetsPath))
        {
            Console.Error.WriteLine($"docgen: warning: {assetsPath} is missing; restore the project to link package types.");
            return [];
        }

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = assets.RootElement;
        var packageFolder = root.GetProperty("packageFolders").EnumerateObject().First().Name;
        var libraries = root.GetProperty("libraries");
        var target = root.GetProperty("targets").EnumerateObject().First(entry => !entry.Name.Contains('/')).Value;
        var assemblies = new List<string>();

        foreach (var package in target.EnumerateObject())
        {
            if (package.Value.GetProperty("type").GetString() != "package") continue;
            if (!package.Value.TryGetProperty("compile", out var compileAssets)) continue;

            var packagePath = libraries.GetProperty(package.Name).GetProperty("path").GetString();

            foreach (var asset in compileAssets.EnumerateObject())
            {
                if (asset.Name.EndsWith("_._", StringComparison.Ordinal)) continue;

                var assemblyPath = Path.Combine(packageFolder, packagePath, asset.Name);
                if (File.Exists(assemblyPath)) assemblies.Add(assemblyPath);
            }
        }

        return assemblies;
    }

    static ApiRepository ReadRepository(string directory)
    {
        var remote = Git(directory, "remote get-url origin");
        if (remote == null) return null;

        if (remote.StartsWith("git@", StringComparison.Ordinal)) remote = "https://" + remote[4..].Replace(':', '/');
        if (remote.EndsWith(".git", StringComparison.Ordinal)) remote = remote[..^4];

        return new ApiRepository
        {
            Url = remote,
            Branch = Git(directory, "rev-parse --abbrev-ref HEAD"),
            Commit = Git(directory, "rev-parse --short HEAD")
        };
    }

    static string Git(string directory, string arguments)
    {
        try
        {
            var start = new ProcessStartInfo("git", $"-C \"{directory}\" {arguments}") { RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
