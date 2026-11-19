using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>BWK001: a CORS policy that combines any origin with AllowCredentials().</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CorsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.CorsAnyOriginWithCredentials);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var builder = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder");
            if (builder is null)
            {
                return;
            }

            // One policy is one builder: the calls of a chain, or the calls on one lambda parameter or variable.
            start.RegisterOperationBlockStartAction(block =>
            {
                var calls = new ConcurrentQueue<(IInvocationOperation Call, object? Root)>();
                block.RegisterOperationAction(ctx =>
                {
                    var call = (IInvocationOperation)ctx.Operation;
                    if (SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, builder))
                    {
                        calls.Enqueue((call, Operations.Root(call)));
                    }
                }, OperationKind.Invocation);

                block.RegisterOperationBlockEndAction(end =>
                {
                    foreach (var anyOrigin in calls.Where(c => IsAnyOrigin(c.Call)))
                    {
                        if (calls.Any(c => c.Call.TargetMethod.Name == "AllowCredentials" && Operations.SameRoot(c.Root, anyOrigin.Root)))
                        {
                            var location = Operations.NameAndArguments(anyOrigin.Call);
                            end.ReportDiagnostic(Diagnostic.Create(
                                Descriptors.CorsAnyOriginWithCredentials, location, location.SourceTree!.GetText().ToString(location.SourceSpan)));
                        }
                    }
                });
            });
        });
    }

    private static bool IsAnyOrigin(IInvocationOperation call) => call.TargetMethod.Name switch
    {
        "AllowAnyOrigin" => true,
        "WithOrigins" => call.Arguments.SelectMany(Values).Any(v => v.ConstantValue is { HasValue: true, Value: "*" }),
        "SetIsOriginAllowed" => call.Arguments.Any(a => AlwaysTrue(a.Value)),
        _ => false,
    };

    private static IEnumerable<IOperation> Values(IArgumentOperation argument) =>
        argument.Value is IArrayCreationOperation { Initializer: { } init } ? init.ElementValues : ImmutableArray.Create(argument.Value);

    // `_ => true` and `origin => { return true; }`.
    private static bool AlwaysTrue(IOperation value) =>
        Operations.Unwrap(value) is IDelegateCreationOperation { Target: IAnonymousFunctionOperation fn } &&
        fn.Body.Operations.Length == 1 &&
        fn.Body.Operations[0] is IReturnOperation { ReturnedValue.ConstantValue: { HasValue: true, Value: true } };
}
