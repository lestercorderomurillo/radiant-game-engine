using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace com.radiant.tools.docgen;

/// <summary>Maps a C# project's public API and XML documentation to JSON consumed by the docs site.</summary>
public static class Program
{
    const string Usage = "usage: Radiant.DocGen --project <path/to/project.csproj> --out <output directory>";

    /// <summary>Runs one generation pass and returns a process exit code.</summary>
    public static int Main(string[] args)
    {
        string projectPath = null, outputDirectory = null;

        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == "--project") projectPath = args[++index];
            else if (args[index] == "--out") outputDirectory = args[++index];
        }

        if (projectPath == null || outputDirectory == null) { Console.Error.WriteLine(Usage); return 2; }

        var stopwatch = Stopwatch.StartNew();
        var project = ProjectSources.Load(Path.GetFullPath(projectPath));
        var compilation = ProjectCompilation.Create(project);
        var model = new ApiExtractor(compilation, project).Extract();

        ApiWriter.Write(model, Path.GetFullPath(outputDirectory));

        int memberCount = model.Types.Sum(type => type.Members.Count);
        Console.WriteLine($"docgen: {model.Types.Count} types, {memberCount} members -> {outputDirectory} ({stopwatch.ElapsedMilliseconds} ms)");
        return 0;
    }
}
