using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>
/// BWK012: SignInManager creates passkey options, and nothing in the project sets IdentityPasskeyOptions.ServerDomain,
/// so the relying party ID comes from the Host header. Info severity: host header validation may happen outside the code.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PasskeyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.PasskeyServerDomainFromHost);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var signInManager = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Identity.SignInManager`1");
            var passkeyOptions = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Identity.IdentityPasskeyOptions");
            if (signInManager is null || passkeyOptions is null)
            {
                return;
            }

            var serverDomainSet = 0;
            var optionsCalls = new ConcurrentBag<Location>();

            start.RegisterOperationAction(ctx =>
            {
                var call = (IInvocationOperation)ctx.Operation;
                if (call.TargetMethod.Name is "MakePasskeyCreationOptionsAsync" or "MakePasskeyRequestOptionsAsync" &&
                    Operations.DerivesFrom(call.TargetMethod.ContainingType.OriginalDefinition, signInManager))
                {
                    optionsCalls.Add(Operations.Name(call));
                }
            }, OperationKind.Invocation);

            start.RegisterOperationAction(ctx =>
            {
                var assignment = (ISimpleAssignmentOperation)ctx.Operation;
                if (assignment.Target is IPropertyReferenceOperation { Property: { Name: "ServerDomain" } property } &&
                    SymbolEqualityComparer.Default.Equals(property.ContainingType, passkeyOptions) &&
                    Operations.Unwrap(assignment.Value).ConstantValue is not { HasValue: true, Value: null })
                {
                    Interlocked.Exchange(ref serverDomainSet, 1);
                }
            }, OperationKind.SimpleAssignment);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (Volatile.Read(ref serverDomainSet) != 0)
                {
                    return;
                }

                foreach (var location in optionsCalls)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptors.PasskeyServerDomainFromHost, location));
                }
            });
        });
    }
}
