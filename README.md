# Bulwark

[![CI](https://github.com/GrantTotinov/Bulwark/actions/workflows/ci.yml/badge.svg)](https://github.com/GrantTotinov/Bulwark/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Bulwark.AspNetCore.Analyzers.svg?label=Bulwark.AspNetCore.Analyzers)](https://www.nuget.org/packages/Bulwark.AspNetCore.Analyzers)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/GrantTotinov/Bulwark/blob/main/LICENSE)

**Catch ASP.NET Core security misconfigurations at build time.** Bulwark is a set of Roslyn analyzers for the mistakes that compile, start and serve requests normally while a protection silently does not apply: a CORS policy that trusts every website with the user's cookies, a rate limiter that never sees the endpoint, antiforgery that skips a request, JWT settings that accept forged tokens.

```bash
dotnet add package Bulwark.AspNetCore.Analyzers --prerelease
```

Then build. Findings show up as warnings in the build output and in the IDE:

```text
Program.cs(12,10): warning BWK001: 'SetIsOriginAllowed(_ => true)' together with AllowCredentials() lets any website send credentialed requests and read the responses
Program.cs(25,5): warning BWK002: UseRateLimiter() is called before UseRouting(), so endpoint policies such as RequireRateLimiting() and [EnableRateLimiting] are never applied
```

MIT. No runtime dependency: the package only adds analyzers to the build. Works with the .NET 8 SDK or Visual Studio 2022 17.8 and later, for C# projects.

## Rules

| ID | Rule | Weakness |
|---|---|---|
| [BWK001](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK001.md) | CORS policy allows credentials from any origin | CWE-942 |
| [BWK002](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK002.md) | `UseRateLimiter()` runs before `UseRouting()` | CWE-307 |
| [BWK003](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK003.md) | Form-bound endpoint skips antiforgery through HTTP method override | CWE-352 |
| [BWK004](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK004.md) | MVC action accepts unsafe requests without antiforgery validation | CWE-352 |
| [BWK005](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK005.md) | JWT signature validation is disabled | CWE-347 |
| [BWK006](https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/BWK006.md) | Identity provider metadata may be fetched over HTTP | CWE-319 |

Every rule page explains the cause, the fix and when a suppression is reasonable.

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
```

[CA5391](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca5391) also looks for MVC actions without antiforgery, but stays silent in projects that never use an antiforgery attribute, which is the case BWK004 is for.

## Configuration

Bulwark rules are ordinary analyzer diagnostics. Change their severity in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.BWK004.severity = error
dotnet_diagnostic.BWK006.severity = none
```

Suppress one finding with `#pragma warning disable BWK001` or `[SuppressMessage("Security", "BWK001", Justification = "...")]`, and say why.

BWK002, BWK003 and BWK004 look at the whole project (for example, whether a global antiforgery filter exists anywhere), so they report on build, not while you type.

## Roadmap

- Code fixes for every rule.
- More rules: cookie `SecurePolicy` and `SameSite`, antiforgery opt-outs on cookie-authenticated endpoints, HSTS, passkey `ServerDomain`, `MapMcp()` without `RequireAuthorization()`, minimal API groups without authorization.
- `dotnet bulwark scan`: a global tool with a console report and SARIF output.
- A GitHub Action that uploads the SARIF to code scanning.

## Contributing

See [CONTRIBUTING.md](https://github.com/GrantTotinov/Bulwark/blob/main/CONTRIBUTING.md). Report vulnerabilities and missed detections that matter privately, see [SECURITY.md](https://github.com/GrantTotinov/Bulwark/blob/main/SECURITY.md).

## License

[MIT](https://github.com/GrantTotinov/Bulwark/blob/main/LICENSE)
