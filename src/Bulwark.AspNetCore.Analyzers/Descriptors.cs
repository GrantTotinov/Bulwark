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

    private static DiagnosticDescriptor Create(string id, string title, string message, string description, params string[] tags) =>
        new(id, title, message, "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true, description,
            $"https://github.com/GrantTotinov/Bulwark/blob/main/docs/rules/{id}.md", tags);
}
