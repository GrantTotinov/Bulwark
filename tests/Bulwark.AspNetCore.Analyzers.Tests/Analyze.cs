using System.Collections.Immutable;
using Bulwark.AspNetCore.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Bulwark.AspNetCore.Analyzers.Tests;

/// <summary>Compiles a web app source against ASP.NET Core and runs every Bulwark analyzer on it.</summary>
internal static class Analyze
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];

    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers =
    [
        .. typeof(CorsAnalyzer).Assembly.GetTypes()
            .Where(t => t.IsDefined(typeof(DiagnosticAnalyzerAttribute), false))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!),
    ];

    // The implicit usings of Microsoft.NET.Sdk.Web.
    private const string WebSdkUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Net.Http.Json;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        """;

    /// <summary>Returns <c>"ID: reported source text"</c> for each Bulwark diagnostic, in source order.</summary>
    public static async Task<string[]> Program(string source)
    {
        var compilation = CSharpCompilation.Create(
            "App",
            [CSharpSyntaxTree.ParseText(source, path: "Program.cs"), CSharpSyntaxTree.ParseText(WebSdkUsings, path: "Usings.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));

        var diagnostics = await compilation.WithAnalyzers(Analyzers).GetAllDiagnosticsAsync(TestContext.Current.CancellationToken);
        var broken = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || d.Id == "AD0001").ToList();
        Assert.True(broken.Count == 0, string.Join(Environment.NewLine, broken));

        return
        [
            .. diagnostics
                .Where(d => d.Id.StartsWith("BWK", StringComparison.Ordinal))
                .OrderBy(d => d.Location.SourceSpan.Start)
                .Select(d => $"{d.Id}: {d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)}"),
        ];
    }

    private static readonly ImmutableArray<CodeFixProvider> Fixers =
    [
        .. typeof(RemoveSettingFix).Assembly.GetTypes()
            .Where(t => t.IsDefined(typeof(ExportCodeFixProviderAttribute), false))
            .Select(t => (CodeFixProvider)Activator.CreateInstance(t)!),
    ];

    /// <summary>
    /// Applies the code fix for the first <paramref name="id"/> diagnostic and returns the new source, or null when no fix
    /// is offered. Checks that the result compiles and no longer has that diagnostic at the same place.
    /// </summary>
    public static async Task<string?> Fix(string source, string id)
    {
        var ct = TestContext.Current.CancellationToken;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "App", "App", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable),
            metadataReferences: References));
        workspace.AddDocument(project.Id, "Usings.cs", SourceText.From(WebSdkUsings));
        var document = workspace.AddDocument(project.Id, "Program.cs", SourceText.From(source));

        var compilation = (await document.Project.GetCompilationAsync(ct))!;
        var diagnostic = (await compilation.WithAnalyzers(Analyzers).GetAnalyzerDiagnosticsAsync(ct))
            .Where(d => d.Id == id)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .First();

        var actions = new List<CodeAction>();
        foreach (var fixer in Fixers.Where(f => f.FixableDiagnosticIds.Contains(id)))
        {
            await fixer.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), ct));
        }

        if (actions.Count == 0)
        {
            return null;
        }

        var operation = (await actions[0].GetOperationsAsync(ct)).OfType<ApplyChangesOperation>().Single();
        var fixedText = (await operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync(ct)).ToString();

        Assert.DoesNotContain($"{id}: {diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)}", await Program(fixedText));
        return fixedText.ReplaceLineEndings("\n");
    }
}
