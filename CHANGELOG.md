# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [0.1.0-preview.1] - 2026-11-19

First preview.

### Added

- **Bulwark.AspNetCore.Analyzers**
  - **BWK001** CORS policy allows credentials from any origin: `SetIsOriginAllowed(_ => true)`, `AllowAnyOrigin()` or `WithOrigins("*")` with `AllowCredentials()`.
  - **BWK002** `UseRateLimiter()` before an explicit `UseRouting()`, in projects with endpoint rate limiting policies.
  - **BWK003** Form-bound `MapDelete` endpoints in apps with form-field HTTP method override, which `UseAntiforgery()` never validates.
  - **BWK004** MVC actions for POST, PUT, PATCH or DELETE without an antiforgery filter, also in projects that never use an antiforgery attribute.
  - **BWK005** JWT signature validation disabled: `RequireSignedTokens = false` or a `SignatureValidator` that only parses the token.
  - **BWK006** `RequireHttpsMetadata = false` outside an `IsDevelopment()` check.
  - **BWK007** `MapMcp()` without `RequireAuthorization()`, a fallback policy or the MCP authorization filters.
  - **BWK008** `CookieSecurePolicy.None` on framework cookies.
  - **BWK009** `HttpOnly = false` on framework cookies.
  - **BWK010** Developer exception page, EF Core error page and migrations endpoint, Blazor `DetailedErrors` and SignalR `EnableDetailedErrors` outside Development.
  - **BWK011** Forwarded headers trusted from any proxy: both `KnownProxies` and `KnownNetworks`/`KnownIPNetworks` cleared.
  - **BWK012** (info) Passkey options created without `IdentityPasskeyOptions.ServerDomain`.
  - Code fixes for BWK002, BWK004, BWK005, BWK006, BWK007, BWK008, BWK009 and BWK010.
  - Detects all four pairs of aspnetcore-security-bench 1.0.0 with no findings on the fixed apps.
- **dotnet-bulwark**: `dotnet bulwark scan` builds a project or solution with the analyzers, prints a report and writes SARIF 2.1.0 for GitHub code scanning, without changing the scanned repository.
- **GitHub Action** `GrantTotinov/Bulwark`: runs the scan and uploads the results to code scanning.

[0.1.0-preview.1]: https://github.com/GrantTotinov/Bulwark/releases/tag/v0.1.0-preview.1
