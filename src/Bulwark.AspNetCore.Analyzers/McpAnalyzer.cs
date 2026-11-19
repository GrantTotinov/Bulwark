using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>
/// BWK007: MapMcp() without authorization. Protected means RequireAuthorization() or AllowAnonymous() on the call or
/// on the group it is mapped on, or, anywhere in the project, a fallback authorization policy or AddAuthorizationFilters().
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class McpAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] Conventions = ["RequireAuthorization", "AllowAnonymous"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.McpWithoutAuthorization);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var mcp = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.McpEndpointRouteBuilderExtensions");
            if (mcp is null)
            {
                return;
            }

            var authorizationOptions = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Authorization.AuthorizationOptions");
            var globallyProtected = 0;
            var unprotected = new ConcurrentBag<Location>();
            void Protected() => Interlocked.Exchange(ref globallyProtected, 1);

            start.RegisterOperationAction(ctx =>
            {
                var call = (IInvocationOperation)ctx.Operation;
                switch (call.TargetMethod.Name)
                {
                    case "MapMcp" when SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, mcp):
                        if (!InChain(call) && !OnProtectedGroup(call))
                        {
                            unprotected.Add(Operations.Name(call));
                        }

                        break;
                    case "SetFallbackPolicy" when !IsNull(call.Arguments.LastOrDefault()?.Value):
                    case "AddAuthorizationFilters":
                        Protected();
                        break;
                }
            }, OperationKind.Invocation);

            start.RegisterOperationAction(ctx =>
            {
                var assignment = (ISimpleAssignmentOperation)ctx.Operation;
                if (assignment.Target is IPropertyReferenceOperation { Property: { Name: "FallbackPolicy" } property } &&
                    SymbolEqualityComparer.Default.Equals(property.ContainingType, authorizationOptions) &&
                    !IsNull(assignment.Value))
                {
                    Protected();
                }
            }, OperationKind.SimpleAssignment);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (Volatile.Read(ref globallyProtected) != 0)
                {
                    return;
                }

                foreach (var location in unprotected)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.McpWithoutAuthorization, location));
                }
            });
        });
    }

    private static bool IsNull(IOperation? value) =>
        value is null || Operations.Unwrap(value).ConstantValue is { HasValue: true, Value: null };

    // app.MapMcp().RequireAuthorization(), or app.MapGroup("/mcp").RequireAuthorization().MapMcp().
    private static bool InChain(IInvocationOperation call)
    {
        for (var parent = call.Parent; parent is IArgumentOperation or IInvocationOperation or IConversionOperation; parent = parent.Parent)
        {
            if (parent is IInvocationOperation outer && Conventions.Contains(outer.TargetMethod.Name))
            {
                return true;
            }
        }

        for (var receiver = Operations.Receiver(call); receiver is not null;)
        {
            receiver = Operations.Unwrap(receiver);
            if (receiver is not IInvocationOperation inner)
            {
                break;
            }

            if (Conventions.Contains(inner.TargetMethod.Name))
            {
                return true;
            }

            receiver = Operations.Receiver(inner);
        }

        return false;
    }

    // var group = app.MapGroup("/mcp").RequireAuthorization(); group.MapMcp();
    // or group.RequireAuthorization() as its own statement in the same method.
    private static bool OnProtectedGroup(IInvocationOperation call)
    {
        if (Operations.Receiver(call) is not { } receiver || Operations.Unwrap(receiver) is not ILocalReferenceOperation { Local: var local })
        {
            return false;
        }

        var declaration = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var body = declaration?.Ancestors().FirstOrDefault(n => n is BaseMethodDeclarationSyntax or CompilationUnitSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
        return body?.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i =>
            i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name, Expression: var target } &&
            Conventions.Contains(name) &&
            (declaration!.Span.Contains(i.Span) || target is IdentifierNameSyntax { Identifier.ValueText: var id } && id == local.Name)) == true;
    }
}
