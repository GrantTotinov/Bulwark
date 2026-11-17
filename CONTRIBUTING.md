# Contributing

Thanks for helping. Bulwark's value is its precision: a warning should almost always mean a real problem. Every rule needs tests that prove both what it reports and what it leaves alone.

## Before you code

- **False positives and missed patterns:** open an issue with the smallest code that shows it.
- **New rules:** open an issue first. Say what the misconfiguration does at runtime, link the official docs that call it insecure, and check that no built-in CA or ASP rule already covers it.

## Rules for rules

- A rule reports configuration that is insecure in every reasonable reading of the code. When in doubt, stay silent.
- An explicit opt-out (`[IgnoreAntiforgeryToken]`, `.DisableAntiforgery()`, a development-only branch) is a decision, not a finding.
- Rules that need the whole project register a compilation-end action and carry `WellKnownDiagnosticTags.CompilationEnd`.
- Each rule has a page in `docs/rules/` with the cause, why it matters, the fix, when to suppress and references, and an entry in `AnalyzerReleases.Unshipped.md`.
- The analyzer builds against Roslyn 4.8 (`netstandard2.0`) so it loads in the .NET 8 SDK. Do not raise that without a reason.
- No runtime dependencies in the package.

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
```

All three must pass. Tests compile small programs against ASP.NET Core and assert the exact diagnostics, see `tests/Bulwark.AspNetCore.Analyzers.Tests/Analyze.cs`. `BenchTests` runs the programs from aspnetcore-security-bench.

## Commits

Use [Conventional Commits](https://www.conventionalcommits.org/): `feat: ...`, `fix: ...`, `docs: ...`, `test: ...`, `chore: ...`.

## License

By contributing you agree that your contribution is licensed under the MIT License.
