# Bulwark

[![CI](https://github.com/GrantTotinov/Bulwark/actions/workflows/ci.yml/badge.svg)](https://github.com/GrantTotinov/Bulwark/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Bulwark.AspNetCore.Analyzers.svg?label=Bulwark.AspNetCore.Analyzers)](https://www.nuget.org/packages/Bulwark.AspNetCore.Analyzers)
[![NuGet](https://img.shields.io/nuget/vpre/dotnet-bulwark.svg?label=dotnet-bulwark)](https://www.nuget.org/packages/dotnet-bulwark)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/GrantTotinov/Bulwark/blob/main/LICENSE)

**Catch ASP.NET Core security misconfigurations at build time.** Bulwark finds the mistakes that compile, start and serve requests normally while a protection silently does not apply: a CORS policy that trusts every website with the user's cookies, a rate limiter that never sees the endpoint, antiforgery that skips a request, an MCP server anyone can call, JWT settings that accept forged tokens.

Use it three ways:

| | |
|---|---|
| **In the IDE and every build** | `dotnet add package Bulwark.AspNetCore.Analyzers --prerelease` |
| **Scan any repo without changing it** | `dotnet tool install -g dotnet-bulwark --prerelease`, then `dotnet bulwark scan` |
| **In GitHub code scanning** | `uses: GrantTotinov/Bulwark@v0.1.0-preview.1` |

MIT, free, no server, no account. The analyzers add nothing to your app at runtime.

## In the IDE and every build

```bash
dotnet add package Bulwark.AspNetCore.Analyzers --prerelease
```

Findings show up as warnings in the build output and as squiggles in Visual Studio, Rider and VS Code. Most rules come with a code fix (Ctrl+.):

```text
Program.cs(12,10): warning BWK001: 'SetIsOriginAllowed(_ => true)' together with AllowCredentials() lets any website send credentialed requests and read the responses
Program.cs(25,5): warning BWK002: UseRateLimiter() is called before UseRouting(), so endpoint policies such as RequireRateLimiting() and [EnableRateLimiting] are never applied
```

Works with the .NET 8 SDK or Visual Studio 2022 17.8 and later, for C# projects.

## Scan a repo with `dotnet bulwark`

```bash
dotnet tool install -g dotnet-bulwark --prerelease
dotnet bulwark scan                          # the project or solution in this folder
dotnet bulwark scan src/Shop.slnx --sarif bulwark.sarif --fail
```

```text
╭────────┬──────────────────────────────┬──────────────────────────────────────────────╮
│ Rule   │ Location                     │ Finding                                      │
├────────┼──────────────────────────────┼──────────────────────────────────────────────┤
│ BWK002 │ src/Api/Program.cs:25:5      │ UseRateLimiter() is called before            │
│        │                              │ UseRouting(), so endpoint policies such as   │
│        │                              │ RequireRateLimiting() and                    │
│        │                              │ [EnableRateLimiting] are never applied       │
╰────────┴──────────────────────────────┴──────────────────────────────────────────────╯
1 finding(s) in 1 file(s).
```

The tool runs a normal `dotnet build` with the Bulwark analyzers added to every C# project. It does not edit project files or add packages. `--sarif` writes the findings as SARIF 2.1.0, and `--fail` exits with code 1 when there are findings. Exit code 2 means the build failed. Needs the .NET 10 runtime.

## In GitHub code scanning

```yaml
name: Bulwark
on: [push, pull_request]

jobs:
  scan:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      security-events: write
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x
      - uses: GrantTotinov/Bulwark@v0.1.0-preview.1
        with:
          path: .                  # project, solution or folder
          fail-on-findings: false  # true to fail the job
```

Findings appear in the repository's **Security → Code scanning** tab and on pull requests, with the rule documentation and a severity.

## Rules

| ID | Rule | Weakness | Code fix |
|---|---|---|---|
| [BWK001](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK001.md) | CORS policy allows credentials from any origin | CWE-942 | |
| [BWK002](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK002.md) | `UseRateLimiter()` runs before `UseRouting()` | CWE-307 | Move the call |
| [BWK003](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK003.md) | Form-bound endpoint skips antiforgery through HTTP method override | CWE-352 | |
| [BWK004](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK004.md) | MVC action accepts unsafe requests without antiforgery validation | CWE-352 | Add `[ValidateAntiForgeryToken]` |
| [BWK005](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK005.md) | JWT signature validation is disabled | CWE-347 | Remove the setting |
| [BWK006](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK006.md) | Identity provider metadata may be fetched over HTTP | CWE-319 | Remove the setting |
| [BWK007](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK007.md) | MCP endpoint is mapped without authorization | CWE-306 | Add `RequireAuthorization()` |
| [BWK008](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK008.md) | Cookie can be sent over HTTP | CWE-614 | Use `CookieSecurePolicy.Always` |
| [BWK009](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK009.md) | Cookie is readable by scripts | CWE-1004 | Remove the setting |
| [BWK010](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK010.md) | Error details are exposed outside Development | CWE-209 | Remove the call or setting |
| [BWK011](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK011.md) | Forwarded headers are accepted from any proxy | CWE-348 | |
| [BWK012](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK012.md) | Passkey relying party ID comes from the Host header (info) | CWE-346 | |

Every rule page explains the cause, the fix and when a suppression is reasonable. Settings inside an `IsDevelopment()` check are not reported.

## Tested against a public benchmark

[aspnetcore-security-bench](https://github.com/GrantTotinov/aspnetcore-security-bench) ([report, DOI 10.5281/zenodo.23822216](https://doi.org/10.5281/zenodo.23822216)) contains pairs of minimal ASP.NET Core apps, one vulnerable and one fixed, for misconfigurations where a protection silently does not apply. Each pair is verified over HTTP on .NET 8 and .NET 10. A tool detects a pair when it reports the vulnerable app and stays silent on the fixed one.

| Pair | Built-in .NET analyzers | Semgrep | CodeQL | Bulwark |
|---|---|---|---|---|
| R01 form-field method override before antiforgery | not detected | not detected | not detected | detected (BWK003) |
| R02 rate limiter before routing | not detected | not detected | not detected | detected (BWK002) |
| R03 MVC action without antiforgery | not detected | not detected | not detected | detected (BWK004) |
| R07 CORS, any origin with credentials | not detected | not detected | not detected | detected (BWK001) |
| **Detected** | **0 of 4** | **0 of 4** | **0 of 4** | **4 of 4** |

Bulwark reports nothing on the four fixed apps. The other columns come from the benchmark report (.NET SDK 10.0.403 with `AnalysisLevel=latest-all`, Semgrep 1.183.0, CodeQL 2.27.3). [`BenchTests`](https://github.com/GrantTotinov/Bulwark/blob/main/tests/Bulwark.AspNetCore.Analyzers.Tests/BenchTests.cs) runs the Bulwark column on every build.

Full disclosure: the benchmark and Bulwark have the same author, and Bulwark was written after the benchmark to close the gaps it found. Read the table as a regression test, not as an independent evaluation. Four pairs say little about precision on real code; reports of false positives are very welcome.

## What Bulwark does not repeat

The .NET SDK already ships rules that are off by default. Turn them on instead of waiting for Bulwark to copy them:

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.CA5404.severity = warning # ValidateIssuer/ValidateAudience/ValidateLifetime/RequireExpirationTime = false
dotnet_diagnostic.CA5382.severity = warning # Response.Cookies.Append with Secure = false
```

[CA5391](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca5391) also looks for MVC actions without antiforgery, but stays silent in projects that never use an antiforgery attribute, which is the case BWK004 is for. `[AllowAnonymous]` overriding `[Authorize]` is ASP0026, built into the SDK.

## Configuration

Bulwark rules are ordinary analyzer diagnostics. Change their severity in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.BWK004.severity = error
dotnet_diagnostic.BWK012.severity = none
```

Suppress one finding with `#pragma warning disable BWK001` or `[SuppressMessage("Security", "BWK001", Justification = "...")]`, and say why. `dotnet bulwark scan` respects the same settings.

BWK002, BWK003, BWK004, BWK007 and BWK012 look at the whole project (for example, whether a global antiforgery filter exists anywhere), so they report on build, not while you type.

## Roadmap

- More rules: antiforgery opt-outs on cookie-authenticated endpoints, HSTS, minimal API groups without authorization, weak Identity password and lockout settings.
- Rules for the .NET 11 cross-origin protection opt-in.
- More pairs in aspnetcore-security-bench, and SonarAnalyzer and Security Code Scan in the comparison.

## Contributing

See [CONTRIBUTING.md](https://github.com/GrantTotinov/Bulwark/blob/main/CONTRIBUTING.md). Report vulnerabilities and missed detections that matter privately, see [SECURITY.md](https://github.com/GrantTotinov/Bulwark/blob/main/SECURITY.md).

## License

[MIT](https://github.com/GrantTotinov/Bulwark/blob/main/LICENSE)
