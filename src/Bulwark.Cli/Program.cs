using System.Reflection;
using System.Text.Json;
using Bulwark.Cli;
using Spectre.Console;

Console.OutputEncoding = System.Text.Encoding.UTF8; // table borders in CI logs and redirected output
var version = typeof(Scanner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
const string Usage = """
    Usage: dotnet bulwark scan [<project | solution | folder>] [--sarif <file>] [--fail]

      --sarif <file>  Also write the findings as SARIF 2.1.0, for GitHub code scanning.
      --fail          Exit with code 1 when there are findings.

    Exit codes: 0 done, 1 findings with --fail, 2 the build or the arguments failed.
    """;

var arguments = args.ToList();
if (arguments.Remove("--version"))
{
    Console.WriteLine(version);
    return 0;
}

if (arguments.Count == 0 || arguments[0] != "scan" || arguments.Contains("--help") || arguments.Contains("-h"))
{
    Console.WriteLine(Usage);
    return arguments.Count == 0 || arguments.Contains("--help") || arguments.Contains("-h") ? 0 : 2;
}

arguments.RemoveAt(0);
var fail = arguments.Remove("--fail");
string? sarif = null;
var sarifIndex = arguments.IndexOf("--sarif");
if (sarifIndex >= 0)
{
    if (sarifIndex + 1 >= arguments.Count)
    {
        Console.Error.WriteLine("--sarif needs a file name.");
        return 2;
    }

    sarif = arguments[sarifIndex + 1];
    arguments.RemoveRange(sarifIndex, 2);
}

if (arguments.Count > 1 || arguments.Any(a => a.StartsWith('-')))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var target = arguments.FirstOrDefault() ?? ".";
AnsiConsole.MarkupLine($"[bold]Bulwark[/] {version}: building [blue]{Markup.Escape(target)}[/] with the security analyzers");

var scan = await AnsiConsole.Status().StartAsync("Building...", _ => Scanner.ScanAsync(target, CancellationToken.None));

if (scan.Findings.Count == 0)
{
    AnsiConsole.MarkupLine("[green]No findings.[/]");
}
else
{
    var table = new Table().Border(TableBorder.Rounded).AddColumns("Rule", "Location", "Finding");
    foreach (var finding in scan.Findings)
    {
        var color = finding.Level == "error" ? "red" : finding.Level == "warning" ? "yellow" : "blue";
        table.AddRow(
            new Markup($"[{color}]{finding.RuleId}[/]"),
            new Text($"{finding.Path}:{finding.Line}:{finding.Column}"),
            new Text(finding.Message));
    }

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($"{scan.Findings.Count} finding(s) in {scan.Findings.Select(f => f.Path).Distinct().Count()} file(s).");
    foreach (var rule in scan.Rules.Where(r => scan.Findings.Any(f => f.RuleId == r.Id)))
    {
        AnsiConsole.MarkupLine($"  [bold]{rule.Id}[/] {Markup.Escape(rule.Title)}: [link]{Markup.Escape(rule.HelpUri)}[/]");
    }
}

if (sarif is not null)
{
    await File.WriteAllTextAsync(sarif, Scanner.ToSarif(scan, version).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    AnsiConsole.MarkupLine($"SARIF written to [blue]{Markup.Escape(sarif)}[/].");
}

if (scan.BuildExitCode != 0)
{
    AnsiConsole.MarkupLine("[red]The build failed, so the results may be incomplete.[/]");
    foreach (var line in scan.BuildOutput.Split('\n').Where(l => l.Contains(": error ", StringComparison.Ordinal)).Distinct().Take(20))
    {
        AnsiConsole.WriteLine(line.TrimEnd());
    }

    return 2;
}

return fail && scan.Findings.Count > 0 ? 1 : 0;
