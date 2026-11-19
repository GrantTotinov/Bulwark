using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>BWK002: UseRateLimiter() called before an explicit UseRouting() in an app with endpoint rate limiting policies.</summary>
/// <remarks>
/// Without an explicit UseRouting(), WebApplication adds routing at the start of the pipeline, so only an
/// explicit call after UseRateLimiter() is a problem. Calls are compared within one method, in source order.
/// Global limiters work before routing, so the rule needs RequireRateLimiting() or [EnableRateLimiting] somewhere.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RateLimiterOrderAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.RateLimiterBeforeRouting);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var compilation = start.Compilation;
            var rateLimiter = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.RateLimiterApplicationBuilderExtensions");
            var routing = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.EndpointRoutingApplicationBuilderExtensions");
            var conventions = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.RateLimiterEndpointConventionBuilderExtensions");
            var enable = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute");
            if (rateLimiter is null || routing is null)
            {
                return;
            }

            var endpointPolicies = 0;
            var limitersBeforeRouting = new ConcurrentBag<Location>();
            void Found() => Interlocked.Exchange(ref endpointPolicies, 1);
            bool Enables(ISymbol symbol) => symbol.GetAttributes().Any(a => Operations.DerivesFrom(a.AttributeClass, enable));

            start.RegisterOperationBlockStartAction(block =>
            {
                var limiters = new ConcurrentQueue<IInvocationOperation>();
                var routings = new ConcurrentQueue<IInvocationOperation>();
                block.RegisterOperationAction(ctx =>
                {
                    var call = (IInvocationOperation)ctx.Operation;
                    if (Is(call, rateLimiter, "UseRateLimiter"))
                    {
                        limiters.Enqueue(call);
                    }
                    else if (Is(call, routing, "UseRouting"))
                    {
                        routings.Enqueue(call);
                    }
                    else if (Is(call, conventions, "RequireRateLimiting"))
                    {
                        Found();
                    }
                }, OperationKind.Invocation);

                block.RegisterOperationBlockEndAction(_ =>
                {
                    foreach (var limiter in limiters)
                    {
                        var position = Operations.Name(limiter).SourceSpan.Start;
                        var root = Operations.Root(limiter);
                        if (routings.Any(r => Operations.Name(r).SourceSpan.Start > position && Operations.SameRoot(Operations.Root(r), root)))
                        {
                            limitersBeforeRouting.Add(Operations.NameAndArguments(limiter));
                        }
                    }
                });
            });

            // [EnableRateLimiting] on controllers, actions and minimal API lambdas.
            start.RegisterSymbolAction(ctx =>
            {
                if (Enables(ctx.Symbol))
                {
                    Found();
                }
            }, SymbolKind.NamedType, SymbolKind.Method);

            start.RegisterOperationAction(ctx =>
            {
                if (Enables(((IAnonymousFunctionOperation)ctx.Operation).Symbol))
                {
                    Found();
                }
            }, OperationKind.AnonymousFunction);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (Volatile.Read(ref endpointPolicies) == 0)
                {
                    return;
                }

                foreach (var location in limitersBeforeRouting)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.RateLimiterBeforeRouting, location));
                }
            });
        });
    }

    private static bool Is(IInvocationOperation call, INamedTypeSymbol? type, string name) =>
        call.TargetMethod.Name == name && SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, type);
}
