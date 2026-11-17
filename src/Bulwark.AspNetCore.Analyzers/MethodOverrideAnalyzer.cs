using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>
/// BWK003: a form-bound MapDelete endpoint in an app that uses form-field HTTP method override.
/// The override turns a cross-site form POST into a DELETE, and UseAntiforgery() does not validate DELETE.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodOverrideAnalyzer : DiagnosticAnalyzer
{
    // Parameters of these types bind the request form without [FromForm].
    private static readonly string[] FormTypes = ["IFormFile", "IFormFileCollection", "IFormCollection"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.MethodOverrideSkipsAntiforgery);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var compilation = start.Compilation;
            var options = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.HttpMethodOverrideOptions");
            var endpoints = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions");
            var fromForm = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Metadata.IFromFormMetadata");
            if (options is null || endpoints is null || fromForm is null)
            {
                return;
            }

            var formTypes = FormTypes
                .Select(name => compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http." + name))
                .ToImmutableArray();
            var formFieldOverride = 0;
            var endpointsWithoutValidation = new ConcurrentBag<Location>();

            start.RegisterOperationAction(ctx =>
            {
                var assignment = (ISimpleAssignmentOperation)ctx.Operation;
                if (assignment.Target is IPropertyReferenceOperation { Property: { Name: "FormFieldName" } property } &&
                    SymbolEqualityComparer.Default.Equals(property.ContainingType, options) &&
                    assignment.Value.ConstantValue is not { HasValue: true, Value: null })
                {
                    Interlocked.Exchange(ref formFieldOverride, 1);
                }
            }, OperationKind.SimpleAssignment);

            // ponytail: MapDelete only. MapMethods with other non-POST/PUT/PATCH verbs is rare; add it when someone needs it.
            start.RegisterOperationAction(ctx =>
            {
                var call = (IInvocationOperation)ctx.Operation;
                if (call.TargetMethod.Name != "MapDelete" || !SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, endpoints))
                {
                    return;
                }

                var handler = call.Arguments.Select(a => Operations.Unwrap(a.Value)).OfType<IDelegateCreationOperation>().FirstOrDefault();
                (IMethodSymbol? Method, SyntaxNode? Body) target = handler?.Target switch
                {
                    IAnonymousFunctionOperation fn => (fn.Symbol, fn.Syntax),
                    IMethodReferenceOperation m => (m.Method, m.Method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ctx.CancellationToken)),
                    _ => default,
                };

                var bindsForm = target.Method?.Parameters.Any(p =>
                    formTypes.Contains(p.Type, SymbolEqualityComparer.Default) ||
                    p.GetAttributes().Any(a => Operations.Implements(a.AttributeClass, fromForm))) == true;
                if (bindsForm && !ValidatesToken(target.Body) && !DisablesAntiforgery(call))
                {
                    endpointsWithoutValidation.Add(Operations.Name(call));
                }
            }, OperationKind.Invocation);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (Volatile.Read(ref formFieldOverride) == 0)
                {
                    return;
                }

                foreach (var location in endpointsWithoutValidation)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.MethodOverrideSkipsAntiforgery, location));
                }
            });
        });
    }

    // The handler checks the token itself, as the ASP.NET Core CSRF docs recommend when the override must run first.
    private static bool ValidatesToken(SyntaxNode? body) =>
        body?.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i =>
            i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "IsRequestValidAsync" or "ValidateRequestAsync" }) == true;

    // app.MapDelete(...).DisableAntiforgery() is an explicit decision.
    private static bool DisablesAntiforgery(IInvocationOperation call)
    {
        for (var parent = call.Parent; parent is IArgumentOperation or IInvocationOperation or IConversionOperation; parent = parent.Parent)
        {
            if (parent is IInvocationOperation { TargetMethod.Name: "DisableAntiforgery" })
            {
                return true;
            }
        }

        return false;
    }
}
