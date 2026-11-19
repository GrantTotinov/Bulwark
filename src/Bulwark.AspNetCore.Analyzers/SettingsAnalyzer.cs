using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>
/// Settings that turn a protection off, checked with one callback per property assignment:
/// BWK005 JWT signature validation, BWK006 HTTP metadata, BWK008 and BWK009 cookie flags, BWK010 error details.
/// All of them are allowed inside an IsDevelopment() check.
/// ValidateIssuer/ValidateAudience/ValidateLifetime/RequireExpirationTime = false are CA5404, so they are not repeated here.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SettingsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.JwtSignatureNotValidated,
        Descriptors.HttpMetadataAllowed,
        Descriptors.CookieWithoutSecure,
        Descriptors.CookieReadableByScripts,
        Descriptors.ErrorDetailsOutsideDevelopment);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var parameters = start.Compilation.GetTypeByMetadataName("Microsoft.IdentityModel.Tokens.TokenValidationParameters");
            var cookieBuilder = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.CookieBuilder");

            start.RegisterOperationAction(ctx =>
            {
                var assignment = (ISimpleAssignmentOperation)ctx.Operation;
                if (assignment.Target is not IPropertyReferenceOperation { Property: var property })
                {
                    return;
                }

                var value = Operations.Unwrap(assignment.Value);
                var type = property.ContainingType;
                var (descriptor, argument) = property.Name switch
                {
                    // JwtBearerOptions, OpenIdConnectOptions and third-party handlers use the same name and meaning.
                    "RequireHttpsMetadata" when Is(value, false) => (Descriptors.HttpMetadataAllowed, null),
                    "RequireSignedTokens" when Is(value, false) && SymbolEqualityComparer.Default.Equals(type, parameters) =>
                        (Descriptors.JwtSignatureNotValidated, "RequireSignedTokens = false"),
                    "SignatureValidator" when SymbolEqualityComparer.Default.Equals(type, parameters) && OnlyParses(value) =>
                        (Descriptors.JwtSignatureNotValidated, "This SignatureValidator"),
                    "SecurePolicy" when value is IFieldReferenceOperation { Field: { Name: "None", ContainingType.Name: "CookieSecurePolicy" } } &&
                                        Operations.DerivesFrom(type, cookieBuilder) => (Descriptors.CookieWithoutSecure, null),
                    "HttpOnly" when Is(value, false) && Operations.DerivesFrom(type, cookieBuilder) => (Descriptors.CookieReadableByScripts, null),
                    "DetailedErrors" when Is(value, true) && type.Name == "CircuitOptions" =>
                        (Descriptors.ErrorDetailsOutsideDevelopment, "DetailedErrors = true"),
                    "EnableDetailedErrors" when Is(value, true) && type.Name.StartsWith("HubOptions", StringComparison.Ordinal) =>
                        (Descriptors.ErrorDetailsOutsideDevelopment, "EnableDetailedErrors = true"),
                    _ => default((DiagnosticDescriptor?, string?)),
                };

                if (descriptor is not null && !Operations.InDevelopmentBranch(assignment.Syntax))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(descriptor, assignment.Syntax.GetLocation(), argument));
                }
            }, OperationKind.SimpleAssignment);

            // app.UseDeveloperExceptionPage(), and the EF Core error page and migrations endpoint.
            start.RegisterOperationAction(ctx =>
            {
                var call = (IInvocationOperation)ctx.Operation;
                var method = call.TargetMethod;
                if (method.Name is "UseDeveloperExceptionPage" or "UseMigrationsEndPoint" or "UseDatabaseErrorPage" &&
                    method.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Builder" &&
                    !Operations.InDevelopmentBranch(call.Syntax))
                {
                    var location = Operations.NameAndArguments(call);
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.ErrorDetailsOutsideDevelopment, location, location.SourceTree!.GetText().ToString(location.SourceSpan)));
                }
            }, OperationKind.Invocation);
        });
    }

    private static bool Is(IOperation value, bool expected) => value.ConstantValue is { HasValue: true, Value: bool actual } && actual == expected;

    // `(token, _) => new JsonWebToken(token)` or `handler.ReadJwtToken(token)`: the body calls nothing, or only Read* parsers.
    // A body that calls anything else may validate, so it is not reported.
    private static bool OnlyParses(IOperation value) =>
        value is IDelegateCreationOperation { Target: IAnonymousFunctionOperation fn } &&
        fn.Body.Descendants().OfType<IInvocationOperation>().All(call =>
            call.TargetMethod.Name.StartsWith("Read", StringComparison.Ordinal));
}
