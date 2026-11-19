using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>
/// BWK011: KnownProxies.Clear() together with KnownNetworks.Clear() or KnownIPNetworks.Clear() on the same
/// ForwardedHeadersOptions. Clearing one list keeps the loopback entry of the other, so only both together trust everyone.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForwardedHeadersAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.ForwardedHeadersFromAnyProxy);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var options = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.ForwardedHeadersOptions");
            if (options is null)
            {
                return;
            }

            start.RegisterOperationBlockStartAction(block =>
            {
                var proxies = new ConcurrentQueue<(IInvocationOperation Call, object? Options)>();
                var networks = new ConcurrentQueue<object?>();
                block.RegisterOperationAction(ctx =>
                {
                    var call = (IInvocationOperation)ctx.Operation;
                    if (call.TargetMethod.Name == "Clear" &&
                        call.Instance is IPropertyReferenceOperation { Property: var list, Instance: var owner } &&
                        SymbolEqualityComparer.Default.Equals(list.ContainingType, options))
                    {
                        if (list.Name == "KnownProxies")
                        {
                            proxies.Enqueue((call, Operations.RootOf(owner)));
                        }
                        else if (list.Name is "KnownNetworks" or "KnownIPNetworks")
                        {
                            networks.Enqueue(Operations.RootOf(owner));
                        }
                    }
                }, OperationKind.Invocation);

                block.RegisterOperationBlockEndAction(end =>
                {
                    foreach (var (call, owner) in proxies)
                    {
                        if (networks.Any(n => Operations.SameRoot(n, owner)))
                        {
                            end.ReportDiagnostic(Diagnostic.Create(Descriptors.ForwardedHeadersFromAnyProxy, call.Syntax.GetLocation()));
                        }
                    }
                });
            });
        });
    }
}
