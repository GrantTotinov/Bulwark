using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bulwark.AspNetCore.Analyzers;

/// <summary>
/// BWK004: a POST, PUT, PATCH or DELETE action on an MVC controller with views, with no antiforgery filter on the
/// action, the controller or globally. Unlike CA5391, it also reports apps that never use an antiforgery attribute.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MvcAntiforgeryAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];
    private static readonly string[] UnsafeVerbs = ["Post", "Put", "Patch", "Delete"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.MvcActionWithoutAntiforgery);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var compilation = start.Compilation;
            INamedTypeSymbol? Type(string name) => compilation.GetTypeByMetadataName(name);

            var controller = Type("Microsoft.AspNetCore.Mvc.Controller");
            if (controller is null)
            {
                return;
            }

            // [ValidateAntiForgeryToken] and [AutoValidateAntiforgeryToken] are filter factories, not IAntiforgeryPolicy.
            // IAntiforgeryPolicy covers [IgnoreAntiforgeryToken] and custom policy filters, IAntiforgeryMetadata [RequireAntiforgeryToken].
            var antiforgery = new[]
            {
                Type("Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute"),
                Type("Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute"),
                Type("Microsoft.AspNetCore.Mvc.ViewFeatures.IAntiforgeryPolicy"),
                Type("Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata"),
            };
            bool IsAntiforgery(ITypeSymbol? type) =>
                antiforgery.Any(t => Operations.DerivesFrom(type, t) || Operations.Implements(type, t));

            var filters = Type("Microsoft.AspNetCore.Mvc.Filters.FilterCollection");
            var apiController = Type("Microsoft.AspNetCore.Mvc.ApiControllerAttribute");
            var nonController = Type("Microsoft.AspNetCore.Mvc.NonControllerAttribute");
            var nonAction = Type("Microsoft.AspNetCore.Mvc.NonActionAttribute");
            var acceptVerbs = Type("Microsoft.AspNetCore.Mvc.AcceptVerbsAttribute");
            var verbAttributes = UnsafeVerbs
                .Select(verb => (Attribute: Type($"Microsoft.AspNetCore.Mvc.Http{verb}Attribute"), Verb: verb.ToUpperInvariant()))
                .ToImmutableArray();

            if (Has(compilation.Assembly.GetAttributes(), apiController))
            {
                return; // [assembly: ApiController]
            }

            bool Protects(ImmutableArray<AttributeData> attributes) => attributes.Any(a => IsAntiforgery(a.AttributeClass));

            string? UnsafeVerb(ImmutableArray<AttributeData> attributes)
            {
                foreach (var attribute in attributes)
                {
                    foreach (var (type, verb) in verbAttributes)
                    {
                        if (Operations.DerivesFrom(attribute.AttributeClass, type))
                        {
                            return verb;
                        }
                    }

                    if (Operations.DerivesFrom(attribute.AttributeClass, acceptVerbs))
                    {
                        var verb = attribute.ConstructorArguments
                            .SelectMany(a => a.Kind == TypedConstantKind.Array ? a.Values : ImmutableArray.Create(a))
                            .Select(c => c.Value as string)
                            .FirstOrDefault(m => m is not null && !SafeMethods.Contains(m, StringComparer.OrdinalIgnoreCase));
                        if (verb is not null)
                        {
                            return verb.ToUpperInvariant();
                        }
                    }
                }

                return null;
            }

            var globalFilter = 0;
            var unprotected = new ConcurrentBag<(IMethodSymbol Action, string Verb)>();

            // options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()), Add(typeof(...)), Add<...>(), AddService<...>().
            start.RegisterOperationAction(ctx =>
            {
                var call = (IInvocationOperation)ctx.Operation;
                if (!Operations.DerivesFrom(call.Instance?.Type, filters))
                {
                    return;
                }

                var added = call.TargetMethod.TypeArguments.Concat(call.Arguments.Select(a => Operations.Unwrap(a.Value) switch
                {
                    IObjectCreationOperation creation => creation.Type,
                    ITypeOfOperation typeOf => typeOf.TypeOperand,
                    _ => null,
                }));
                if (added.Any(IsAntiforgery))
                {
                    Interlocked.Exchange(ref globalFilter, 1);
                }
            }, OperationKind.Invocation);

            start.RegisterSymbolAction(ctx =>
            {
                var type = (INamedTypeSymbol)ctx.Symbol;
                if (type.IsAbstract || !Operations.DerivesFrom(type, controller))
                {
                    return;
                }

                for (var t = type; t is not null; t = t.BaseType)
                {
                    var attributes = t.GetAttributes();
                    if (Has(attributes, apiController) || Has(attributes, nonController) || Protects(attributes))
                    {
                        return;
                    }
                }

                foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public || method.IsStatic)
                    {
                        continue;
                    }

                    var attributes = method.GetAttributes();
                    if (!Has(attributes, nonAction) && !Protects(attributes) && UnsafeVerb(attributes) is { } verb)
                    {
                        unprotected.Add((method, verb));
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(ctx =>
            {
                if (Volatile.Read(ref globalFilter) != 0)
                {
                    return;
                }

                foreach (var (action, verb) in unprotected)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.MvcActionWithoutAntiforgery, action.Locations[0], $"{action.ContainingType.Name}.{action.Name}", verb));
                }
            });
        });
    }

    private static bool Has(ImmutableArray<AttributeData> attributes, INamedTypeSymbol? type) =>
        type is not null && attributes.Any(a => Operations.DerivesFrom(a.AttributeClass, type));
}
