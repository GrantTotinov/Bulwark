using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>
/// BWK005: JWT signature validation turned off. BWK006: RequireHttpsMetadata = false outside an IsDevelopment() check.
/// ValidateIssuer/ValidateAudience/ValidateLifetime/RequireExpirationTime = false are CA5404, so they are not repeated here.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TokenValidationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.JwtSignatureNotValidated, Descriptors.HttpMetadataAllowed);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var parameters = start.Compilation.GetTypeByMetadataName("Microsoft.IdentityModel.Tokens.TokenValidationParameters");
            start.RegisterOperationAction(ctx =>
            {
                var assignment = (ISimpleAssignmentOperation)ctx.Operation;
                if (assignment.Target is not IPropertyReferenceOperation { Property: var property })
                {
                    return;
                }

                var isFalse = assignment.Value.ConstantValue is { HasValue: true, Value: false };
                var location = assignment.Syntax.GetLocation();

                // JwtBearerOptions, OpenIdConnectOptions and third-party handlers use the same name and meaning.
                if (property.Name == "RequireHttpsMetadata" && isFalse && !InDevelopmentBranch(assignment.Syntax))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.HttpMetadataAllowed, location));
                }
                else if (SymbolEqualityComparer.Default.Equals(property.ContainingType, parameters))
                {
                    if (property.Name == "RequireSignedTokens" && isFalse)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.JwtSignatureNotValidated, location, "RequireSignedTokens = false"));
                    }
                    else if (property.Name == "SignatureValidator" && OnlyParses(assignment.Value))
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.JwtSignatureNotValidated, location, "This SignatureValidator"));
                    }
                }
            }, OperationKind.SimpleAssignment);
        });
    }

    // `(token, _) => new JsonWebToken(token)` or `handler.ReadJwtToken(token)`: the body calls nothing, or only Read* parsers.
    // A body that calls anything else may validate, so it is not reported.
    private static bool OnlyParses(IOperation value) =>
        Operations.Unwrap(value) is IDelegateCreationOperation { Target: IAnonymousFunctionOperation fn } &&
        fn.Body.Descendants().OfType<IInvocationOperation>().All(call =>
            call.TargetMethod.Name.StartsWith("Read", StringComparison.Ordinal));

    private static bool InDevelopmentBranch(SyntaxNode node) =>
        node.Ancestors().OfType<IfStatementSyntax>().Any(statement =>
            statement.Statement.Span.Contains(node.Span) &&
            statement.Condition.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "IsDevelopment" } &&
                call.Parent is not PrefixUnaryExpressionSyntax));
}
