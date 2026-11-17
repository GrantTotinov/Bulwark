using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

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
}
