using Microsoft.CodeAnalysis;

namespace Bulwark.AspNetCore.Analyzers;

internal static class Descriptors
{
    public static readonly DiagnosticDescriptor CorsAnyOriginWithCredentials = Create(
        "BWK001",
        "CORS policy allows credentials from any origin",
        "'{0}' together with AllowCredentials() lets any website send credentialed requests and read the responses",
        "A CORS policy that accepts every origin and also allows credentials lets any website make requests with the user's cookies and read the responses. ASP.NET Core refuses AllowAnyOrigin() with AllowCredentials(), but SetIsOriginAllowed(_ => true) gets around that check. List the trusted origins with WithOrigins(...) instead.",
        "CWE-942");

    public static readonly DiagnosticDescriptor RateLimiterBeforeRouting = Create(
        "BWK002",
        "UseRateLimiter() runs before UseRouting()",
        "UseRateLimiter() is called before UseRouting(), so endpoint policies such as RequireRateLimiting() and [EnableRateLimiting] are never applied",
        "The rate limiting middleware reads the selected endpoint to find its policy. Before UseRouting() no endpoint is selected, so endpoint-specific limits silently do not apply: no exception and no log entry. Call UseRateLimiter() after UseRouting().",
        "CWE-307",
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor MethodOverrideSkipsAntiforgery = Create(
        "BWK003",
        "Form-bound endpoint skips antiforgery through HTTP method override",
        "This DELETE endpoint binds form data and form-field method override makes it reachable from a cross-site form, but UseAntiforgery() validates only POST, PUT and PATCH requests",
        "With UseHttpMethodOverride and a FormFieldName, a form POST with _method=DELETE is routed to a DELETE endpoint. The endpoint carries antiforgery metadata because it binds form data, but the antiforgery middleware validates only POST, PUT and PATCH, so the token is never checked. Validate the token in the handler with IAntiforgery.ValidateRequestAsync, or do not bind form data on DELETE endpoints.",
        "CWE-352",
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor MvcActionWithoutAntiforgery = Create(
        "BWK004",
        "MVC action accepts unsafe requests without antiforgery validation",
        "Action '{0}' handles {1} requests but no antiforgery filter applies to it, so it accepts requests without a token",
        "MVC validates antiforgery tokens only where a filter asks for it, unlike Razor Pages and form-bound minimal APIs. Add AutoValidateAntiforgeryTokenAttribute as a global filter, or [ValidateAntiForgeryToken] to the controller or action. Use [IgnoreAntiforgeryToken] or [ApiController] for endpoints that never use cookie authentication.",
        "CWE-352",
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor JwtSignatureNotValidated = Create(
        "BWK005",
        "JWT signature validation is disabled",
        "{0} lets the handler accept tokens without a valid signature",
        "With RequireSignedTokens = false, or a SignatureValidator that only parses the token, anyone can forge a token with any claims. Remove the setting and let the handler validate signatures with the issuer's keys.",
        "CWE-347");

    public static readonly DiagnosticDescriptor HttpMetadataAllowed = Create(
        "BWK006",
        "Identity provider metadata may be fetched over HTTP",
        "RequireHttpsMetadata = false allows the handler to download the identity provider's metadata and signing keys over plain HTTP",
        "A network attacker who can intercept an HTTP metadata request can replace the signing keys and forge tokens that the app accepts. Remove the setting, or limit it to the Development environment.",
        "CWE-319");

    public static readonly DiagnosticDescriptor McpWithoutAuthorization = Create(
        "BWK007",
        "MCP endpoint is mapped without authorization",
        "MapMcp() exposes the server's tools to any client that can reach it; add RequireAuthorization(), or AllowAnonymous() if the server is meant to be public",
        "An MCP endpoint lets clients list and call the server's tools, which often read data or take actions. Without RequireAuthorization(), a fallback authorization policy or the MCP authorization filters, anyone who can reach the URL can use them. Add RequireAuthorization() to the MapMcp() call, or AllowAnonymous() to document that the server is public.",
        "CWE-306",
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor CookieWithoutSecure = Create(
        "BWK008",
        "Cookie can be sent over HTTP",
        "CookieSecurePolicy.None lets the browser send this cookie over plain HTTP, where anyone on the network can read it",
        "A cookie without the Secure attribute is sent on every HTTP request to the site, for example after a downgrade or a mistyped http:// link. For authentication, session and antiforgery cookies, that hands the session to anyone on the network. Use CookieSecurePolicy.Always.",
        "CWE-614");

    public static readonly DiagnosticDescriptor CookieReadableByScripts = Create(
        "BWK009",
        "Cookie is readable by scripts",
        "HttpOnly = false lets any script on the page read this cookie, so a cross-site scripting flaw can steal it",
        "Authentication, session and antiforgery cookies do not need to be read by JavaScript. With HttpOnly = false, one injected script can copy the cookie and use the session elsewhere. Keep HttpOnly = true.",
        "CWE-1004");

    public static readonly DiagnosticDescriptor ErrorDetailsOutsideDevelopment = Create(
        "BWK010",
        "Error details are exposed outside Development",
        "{0} sends exception details to clients outside the Development environment",
        "Stack traces, SQL, file paths and configuration values in error responses help attackers map the app. WebApplication already shows the developer exception page in Development. Remove the call or setting, or limit it to the Development environment.",
        "CWE-209");

    public static readonly DiagnosticDescriptor ForwardedHeadersFromAnyProxy = Create(
        "BWK011",
        "Forwarded headers are accepted from any proxy",
        "Clearing both KnownProxies and KnownNetworks makes the app trust X-Forwarded-* headers from any client, so callers can spoof their IP address and scheme",
        "The forwarded headers middleware only applies X-Forwarded-For, X-Forwarded-Proto and X-Forwarded-Host from trusted proxies. With both lists empty, any client can set them, which defeats IP-based rate limits, allow lists and logging, and can fake HTTPS. Add the proxy's address to KnownProxies or its network to KnownIPNetworks.",
        "CWE-348");

    public static readonly DiagnosticDescriptor PasskeyServerDomainFromHost = Create(
        "BWK012",
        "Passkey relying party ID comes from the Host header",
        "IdentityPasskeyOptions.ServerDomain is not set, so the passkey relying party ID is taken from the request's Host header",
        "When ServerDomain is null, ASP.NET Core Identity derives the WebAuthn relying party ID from the Host header. Unless the hosting environment validates Host headers, a request with another host can scope passkey operations to a domain the attacker controls. Set ServerDomain to the app's domain.",
        "CWE-346",
        DiagnosticSeverity.Info,
        WellKnownDiagnosticTags.CompilationEnd);

    private static DiagnosticDescriptor Create(string id, string title, string message, string description, params string[] tags) =>
        Create(id, title, message, description, tags[0], DiagnosticSeverity.Warning, tags.Skip(1).ToArray());

    private static DiagnosticDescriptor Create(
        string id, string title, string message, string description, string cwe, DiagnosticSeverity severity, params string[] tags) =>
        new(id, title, message, "Security", severity, isEnabledByDefault: true, description,
            $"https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/{id}.md", [cwe, .. tags]);
}
