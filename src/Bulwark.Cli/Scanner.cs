using System.Diagnostics;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bulwark.Cli;

internal sealed record Rule(string Id, string Title, string Description, string HelpUri, string[] Tags);

internal sealed record Finding(string RuleId, string Level, string Message, string Path, int Line, int Column, int EndLine, int EndColumn);

internal sealed record ScanResult(IReadOnlyList<Finding> Findings, IReadOnlyList<Rule> Rules, int BuildExitCode, string BuildOutput);

/// <summary>
/// Builds the target with the Bulwark analyzer injected and the compiler's SARIF log turned on, then reads the logs.
/// Nothing in the scanned repository changes except its normal build output.
/// </summary>
internal static class Scanner
{
    public static async Task<ScanResult> ScanAsync(string target, CancellationToken ct)
    {
        var output = Directory.CreateTempSubdirectory("bulwark-");
        try
        {
            var analyzer = Path.Combine(AppContext.BaseDirectory, "Bulwark.AspNetCore.Analyzers.dll");
            var targets = Path.Combine(output.FullName, "Bulwark.targets");
            await File.WriteAllTextAsync(targets, $"""
                <Project>
                  <PropertyGroup Condition="'$(Language)' == 'C#'">
                    <ErrorLog>{SecurityElement.Escape(output.FullName)}/$(MSBuildProjectName)-$(TargetFramework)-$([System.Guid]::NewGuid().ToString('N')).sarif,version=2.1</ErrorLog>
                  </PropertyGroup>
                  <Target Name="BulwarkAddAnalyzer" BeforeTargets="CoreCompile" Condition="'$(Language)' == 'C#'">
                    <ItemGroup>
                      <Analyzer Remove="@(Analyzer)" Condition="'%(Filename)' == 'Bulwark.AspNetCore.Analyzers'" />
                      <Analyzer Include="{SecurityElement.Escape(analyzer)}" />
                    </ItemGroup>
                  </Target>
                </Project>
                """, ct);

            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            string[] arguments =
            [
                "build", target, "--no-incremental", "-nologo", "-v:q",
                $"-p:CustomAfterMicrosoftCommonTargets={targets}",
                // A scan must see every project, so warnings must not stop the build halfway.
                "-p:TreatWarningsAsErrors=false", "-p:MSBuildTreatWarningsAsErrors=false",
                "-p:RunAnalyzers=true", "-p:RunAnalyzersDuringBuild=true",
            ];
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var (findings, rules) = Read(output.GetFiles("*.sarif"));
            return new ScanResult(findings, rules, process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    private static (List<Finding>, List<Rule>) Read(IEnumerable<FileInfo> logs)
    {
        var findings = new List<Finding>();
        var rules = new Dictionary<string, Rule>();
        foreach (var log in logs)
        {
            using var stream = log.OpenRead();
            using var document = JsonDocument.Parse(stream);
            foreach (var run in document.RootElement.GetProperty("runs").EnumerateArray())
            {
                foreach (var rule in Array(run.GetProperty("tool").GetProperty("driver"), "rules"))
                {
                    var id = rule.GetProperty("id").GetString()!;
                    if (id.StartsWith("BWK", StringComparison.Ordinal))
                    {
                        rules.TryAdd(id, new Rule(
                            id,
                            Text(rule, "shortDescription"),
                            Text(rule, "fullDescription"),
                            rule.TryGetProperty("helpUri", out var help) ? help.GetString()! : "",
                            rule.TryGetProperty("properties", out var p) ? [.. Array(p, "tags").Select(t => t.GetString()!)] : []));
                    }
                }

                foreach (var result in Array(run, "results"))
                {
                    var id = result.GetProperty("ruleId").GetString()!;
                    if (!id.StartsWith("BWK", StringComparison.Ordinal) ||
                        result.TryGetProperty("suppressions", out _) ||
                        Array(result, "locations").FirstOrDefault() is not { ValueKind: JsonValueKind.Object } location)
                    {
                        continue;
                    }

                    var physical = location.GetProperty("physicalLocation");
                    var region = physical.GetProperty("region");
                    findings.Add(new Finding(
                        id,
                        result.TryGetProperty("level", out var level) ? level.GetString()! : "warning",
                        Text(result, "message"),
                        RelativePath(physical.GetProperty("artifactLocation").GetProperty("uri").GetString()!),
                        Int(region, "startLine"),
                        Int(region, "startColumn"),
                        Int(region, "endLine"),
                        Int(region, "endColumn")));
                }
            }
        }

        // Multi-targeted projects report the same finding once per target framework.
        return ([.. findings.Distinct().OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Column)],
                [.. rules.Values.OrderBy(r => r.Id, StringComparer.Ordinal)]);
    }

    private static JsonElement[] Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetProperty("text", out var text) ? text.GetString()! : "";

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    // Paths relative to the current directory, which is the repository root in CI, as GitHub code scanning expects.
    private static string RelativePath(string uri)
    {
        var path = Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : uri;
        var relative = System.IO.Path.GetRelativePath(Environment.CurrentDirectory, path);
        return (relative.StartsWith("..", StringComparison.Ordinal) ? path : relative).Replace('\\', '/');
    }

    /// <summary>One SARIF 2.1.0 run with only Bulwark rules, ready for github/codeql-action/upload-sarif.</summary>
    public static JsonObject ToSarif(ScanResult scan, string version) => new()
    {
        ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
        ["version"] = "2.1.0",
        ["runs"] = new JsonArray(new JsonObject
        {
            ["tool"] = new JsonObject
            {
                ["driver"] = new JsonObject
                {
                    ["name"] = "Bulwark",
                    ["semanticVersion"] = version,
                    ["informationUri"] = "https://github.com/GrantTotinov/Bulwark",
                    ["rules"] = new JsonArray([.. scan.Rules.Select(r => (JsonNode)new JsonObject
                    {
                        ["id"] = r.Id,
                        ["name"] = r.Id,
                        ["shortDescription"] = new JsonObject { ["text"] = r.Title },
                        ["fullDescription"] = new JsonObject { ["text"] = r.Description },
                        ["helpUri"] = r.HelpUri,
                        ["help"] = new JsonObject { ["text"] = r.Description, ["markdown"] = $"{r.Description}\n\n[Rule documentation]({r.HelpUri})" },
                        ["properties"] = new JsonObject
                        {
                            ["tags"] = new JsonArray([.. r.Tags.Prepend("security").Select(t => (JsonNode)JsonValue.Create(t)!)]),
                            ["security-severity"] = SecuritySeverity(r.Id),
                        },
                    })]),
                },
            },
            ["results"] = new JsonArray([.. scan.Findings.Select(f => (JsonNode)new JsonObject
            {
                ["ruleId"] = f.RuleId,
                ["level"] = f.Level,
                ["message"] = new JsonObject { ["text"] = f.Message },
                ["locations"] = new JsonArray(new JsonObject
                {
                    ["physicalLocation"] = new JsonObject
                    {
                        ["artifactLocation"] = new JsonObject { ["uri"] = f.Path },
                        ["region"] = new JsonObject
                        {
                            ["startLine"] = f.Line,
                            ["startColumn"] = f.Column,
                            ["endLine"] = f.EndLine,
                            ["endColumn"] = f.EndColumn,
                        },
                    },
                }),
            })]),
        }),
    };

    // GitHub code scanning shows 9.0+ as critical, 7.0+ as high, 4.0+ as medium.
    private static string SecuritySeverity(string id) => id switch
    {
        "BWK005" or "BWK007" => "9.1",
        "BWK001" => "8.1",
        "BWK003" or "BWK004" or "BWK006" => "7.1",
        "BWK011" => "6.5",
        "BWK002" or "BWK008" or "BWK009" or "BWK010" => "5.3",
        _ => "4.3",
    };
}
