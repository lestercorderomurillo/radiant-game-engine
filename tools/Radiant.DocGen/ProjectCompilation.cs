using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace com.radiant.tools.docgen;

/// <summary>Builds a semantic-only Roslyn compilation of a project's sources.</summary>
public static class ProjectCompilation
{
    /// <summary>Parses every source with documentation comments and binds it against framework and package assemblies.</summary>
    public static Compilation Create(ProjectSources project)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse);
        var syntaxTrees = project.SourceFiles.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path));
        var references = FrameworkAssemblies().Concat(project.PackageAssemblies).Distinct().Select(path => MetadataReference.CreateFromFile(path));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: project.AllowUnsafe);
        var compilation = CSharpCompilation.Create(project.Name, syntaxTrees, references, options);

        ReportDeclarationErrors(compilation);
        return compilation;
    }

    static IEnumerable<string> FrameworkAssemblies()
    {
        var runtimeDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return trustedAssemblies.Where(path => Path.GetFullPath(Path.GetDirectoryName(path) + Path.DirectorySeparatorChar) == runtimeDirectory);
    }

    /// <summary>Source generators do not run here, so their missing partial implementations are expected.</summary>
    static readonly string[] GeneratorDiagnostics = ["CS8795", "CS8796"];

    static void ReportDeclarationErrors(Compilation compilation)
    {
        var errors = compilation.GetDeclarationDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && !GeneratorDiagnostics.Contains(diagnostic.Id))
            .ToList();
        if (errors.Count == 0) return;

        Console.Error.WriteLine($"docgen: warning: {errors.Count} declaration errors; affected types may render without links.");
        foreach (var error in errors.Take(3)) Console.Error.WriteLine($"  {error}");
    }
}
