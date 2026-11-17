# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

First preview.

### Added

- **BWK001** CORS policy allows credentials from any origin: `SetIsOriginAllowed(_ => true)`, `AllowAnyOrigin()` or `WithOrigins("*")` with `AllowCredentials()`.
- **BWK002** `UseRateLimiter()` before an explicit `UseRouting()`, in projects with endpoint rate limiting policies.
- **BWK003** Form-bound `MapDelete` endpoints in apps with form-field HTTP method override, which `UseAntiforgery()` never validates.
- **BWK004** MVC actions for POST, PUT, PATCH or DELETE without an antiforgery filter, also in projects that never use an antiforgery attribute.
- **BWK005** JWT signature validation disabled: `RequireSignedTokens = false` or a `SignatureValidator` that only parses the token.
- **BWK006** `RequireHttpsMetadata = false` outside an `IsDevelopment()` check.
- Detects all four pairs of aspnetcore-security-bench 1.0.0 with no findings on the fixed apps.
