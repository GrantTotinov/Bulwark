using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Bulwark.AspNetCore.CodeFixes;

/// <summary>Base for fixes that rewrite one node at the diagnostic location.</summary>
public abstract class SyntaxFix : CodeFixProvider
{
    protected abstract string Title { get; }

    /// <summary>Returns the new root, or null when the code has a shape the fix does not handle.</summary>
    protected abstract SyntaxNode? Fix(SyntaxNode root, SyntaxNode node);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (Fix(root, node) is { } fixedRoot)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(fixedRoot)), equivalenceKey: Title),
                    diagnostic);
            }
        }
    }
}

/// <summary>BWK005, BWK006, BWK009, BWK010: removes the assignment or the call. The defaults are the secure values.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveSettingFix)), Shared]
public sealed class RemoveSettingFix : SyntaxFix
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BWK005", "BWK006", "BWK009", "BWK010");

    protected override string Title => "Remove the insecure setting";

    protected override SyntaxNode? Fix(SyntaxNode root, SyntaxNode node)
    {
        var target = node.AncestorsAndSelf().FirstOrDefault(n => n is AssignmentExpressionSyntax or InvocationExpressionSyntax);
        SyntaxNode? removable = target?.Parent switch
        {
            InitializerExpressionSyntax => target, // new TokenValidationParameters { RequireSignedTokens = false }
            ExpressionStatementSyntax { Parent: GlobalStatementSyntax global } => global,
            ExpressionStatementSyntax statement => statement,
            _ => null, // o => o.RequireHttpsMetadata = false: removing would leave nothing
        };
        return removable is null ? null : root.RemoveNode(removable, SyntaxRemoveOptions.KeepUnbalancedDirectives);
    }
}

/// <summary>BWK008: CookieSecurePolicy.None to CookieSecurePolicy.Always.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SecureCookieFix)), Shared]
public sealed class SecureCookieFix : SyntaxFix
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BWK008");

    protected override string Title => "Use CookieSecurePolicy.Always";

    protected override SyntaxNode? Fix(SyntaxNode root, SyntaxNode node) =>
        node.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault()?.Right is MemberAccessExpressionSyntax value
            ? root.ReplaceNode(value.Name, IdentifierName("Always").WithTriviaFrom(value.Name))
            : null;
}

/// <summary>BWK007: app.MapMcp() to app.MapMcp().RequireAuthorization().</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequireAuthorizationFix)), Shared]
public sealed class RequireAuthorizationFix : SyntaxFix
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BWK007");

    protected override string Title => "Add RequireAuthorization()";

    protected override SyntaxNode? Fix(SyntaxNode root, SyntaxNode node)
    {
        if (node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault() is not { } call)
        {
            return null;
        }

        var secured = InvocationExpression(MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression, call.WithoutTrailingTrivia(), IdentifierName("RequireAuthorization")))
            .WithTrailingTrivia(call.GetTrailingTrivia());
        return root.ReplaceNode(call, secured);
    }
}

/// <summary>BWK004: adds ValidateAntiForgeryToken next to the verb attribute: [HttpPost, ValidateAntiForgeryToken].</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ValidateAntiforgeryFix)), Shared]
public sealed class ValidateAntiforgeryFix : SyntaxFix
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BWK004");

    protected override string Title => "Add [ValidateAntiForgeryToken]";

    protected override SyntaxNode? Fix(SyntaxNode root, SyntaxNode node)
    {
        // The rule only reports actions with a verb attribute, so there is always a list to extend.
        var lists = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.AttributeLists;
        var list = lists?.FirstOrDefault(l => l.Attributes.Any(a => a.Name.ToString() is var name &&
            (name.Contains("Http") || name.Contains("AcceptVerbs")))) ?? lists?.FirstOrDefault();
        if (list is null)
        {
            return null;
        }

        var attribute = Attribute(ParseName("Microsoft.AspNetCore.Mvc.ValidateAntiForgeryToken").WithAdditionalAnnotations(Simplifier.Annotation));
        return root.ReplaceNode(list, list.AddAttributes(attribute).WithAdditionalAnnotations(Formatter.Annotation));
    }
}

/// <summary>BWK002: moves the UseRateLimiter() statement to just after the UseRouting() statement.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MoveRateLimiterFix)), Shared]
public sealed class MoveRateLimiterFix : SyntaxFix
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BWK002");

    protected override string Title => "Move UseRateLimiter() after UseRouting()";

    protected override SyntaxNode? Fix(SyntaxNode root, SyntaxNode node)
    {
        // Only `app.UseRateLimiter();` as its own statement, in a block or at the top level. Chains are left alone.
        var call = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (call?.Parent is not ExpressionStatementSyntax statement)
        {
            return null;
        }

        var limiter = statement.Parent is GlobalStatementSyntax global ? global : (SyntaxNode)statement;
        var items = limiter.Parent switch
        {
            BlockSyntax block => block.Statements.Cast<SyntaxNode>().ToList(),
            CompilationUnitSyntax unit => unit.Members.Cast<SyntaxNode>().ToList(),
            _ => null,
        };
        var from = items?.IndexOf(limiter) ?? -1;
        var to = items?.FindIndex(from + 1, i => i.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsUseRouting)) ?? -1;
        if (items is null || from < 0 || to < 0)
        {
            return null;
        }

        // Blank lines above the limiter stay where they are; its comments move with it.
        var leading = limiter.GetLeadingTrivia();
        var blank = 0;
        for (var i = 0; i < leading.Count && (leading[i].IsKind(SyntaxKind.EndOfLineTrivia) || leading[i].IsKind(SyntaxKind.WhitespaceTrivia)); i++)
        {
            if (leading[i].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                blank = i + 1;
            }
        }

        var moved = limiter.WithLeadingTrivia(leading.Skip(blank));
        items.RemoveAt(from);
        items[from] = items[from].WithLeadingTrivia(leading.Take(blank).Concat(items[from].GetLeadingTrivia()));
        items.Insert(to, moved);

        return limiter.Parent switch
        {
            BlockSyntax block => root.ReplaceNode(block, block.WithStatements(List(items.Cast<StatementSyntax>()))),
            CompilationUnitSyntax unit => unit.WithMembers(List(items.Cast<MemberDeclarationSyntax>())),
            _ => null,
        };
    }

    private static bool IsUseRouting(InvocationExpressionSyntax call) =>
        call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "UseRouting" };
}
