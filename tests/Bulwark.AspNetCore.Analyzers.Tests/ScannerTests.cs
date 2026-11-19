using Bulwark.Cli;

namespace Bulwark.AspNetCore.Analyzers.Tests;

/// <summary>Runs a real `dotnet build` of a small web project, like `dotnet bulwark scan` does.</summary>
public class ScannerTests
{
    [Fact]
    public async Task Scans_a_project_without_the_package_and_writes_sarif()
    {
        var project = Directory.CreateTempSubdirectory("bulwark-scan-");
        try
        {
            File.WriteAllText(Path.Combine(project.FullName, "App.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                </Project>
                """);
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "bench", "R07-cors-any-origin-credentials", "Vulnerable.cs"),
                Path.Combine(project.FullName, "Program.cs"));

            var scan = await Scanner.ScanAsync(project.FullName, TestContext.Current.CancellationToken);

            Assert.True(scan.BuildExitCode == 0, scan.BuildOutput);
            var finding = Assert.Single(scan.Findings);
            Assert.Equal(("BWK001", 12, 10), (finding.RuleId, finding.Line, finding.Column));
            Assert.EndsWith("Program.cs", finding.Path, StringComparison.Ordinal);
            Assert.Equal(12, scan.Rules.Count);

            var run = Scanner.ToSarif(scan, "1.0.0")["runs"]![0]!;
            Assert.Equal("BWK001", (string?)run["results"]![0]!["ruleId"]);
            var rule = run["tool"]!["driver"]!["rules"]!.AsArray().Single(r => (string?)r!["id"] == "BWK001")!;
            Assert.Equal("8.1", (string?)rule["properties"]!["security-severity"]);
            Assert.EndsWith("docs/rules/BWK001.md", (string?)rule["helpUri"], StringComparison.Ordinal);
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }
}
