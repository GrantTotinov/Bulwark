using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Bulwark.AspNetCore.Analyzers;

internal static class Operations
{
    /// <summary>The receiver of a call: the instance, or the <c>this</c> argument of an extension method.</summary>
    public static IOperation? Receiver(IInvocationOperation call) =>
        call.Instance ?? (call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0 ? call.Arguments[0].Value : null);

    /// <summary>
    /// The variable a call chain starts from: <c>app</c> in <c>app.UseRateLimiter().UseRouting()</c>,
    /// <c>policy</c> in <c>policy => policy.AllowCredentials()</c>. Falls back to the first operation of the chain.
    /// </summary>
    public static object? Root(IInvocationOperation call) => RootOf(Receiver(call));

    /// <summary>The variable an expression starts from, walking down conversions and call chains.</summary>
    public static object? RootOf(IOperation? op)
    {
        while (true)
        {
            if (op is IConversionOperation conversion)
            {
                op = conversion.Operand;
            }
            else if (op is IInvocationOperation inner && Receiver(inner) is { } receiver)
            {
                op = receiver;
            }
            else
            {
                break;
            }
        }

        return op switch
        {
            IParameterReferenceOperation p => p.Parameter,
            ILocalReferenceOperation l => l.Local,
            IFieldReferenceOperation f => f.Field,
            IPropertyReferenceOperation p => p.Property,
            _ => op,
        };
    }

    public static bool SameRoot(object? a, object? b) =>
        a is ISymbol x && b is ISymbol y ? SymbolEqualityComparer.Default.Equals(x, y) : a is not null && ReferenceEquals(a, b);

    public static IOperation Unwrap(IOperation op)
    {
        while (op is IConversionOperation conversion)
        {
            op = conversion.Operand;
        }

        return op;
    }

    /// <summary>The method name and arguments of a call, without the receiver: <c>UseRouting()</c> in <c>app.UseRouting()</c>.</summary>
    public static Location NameAndArguments(IInvocationOperation call) =>
        call.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation
            ? Location.Create(invocation.SyntaxTree, TextSpan.FromBounds(member.Name.SpanStart, invocation.Span.End))
            : call.Syntax.GetLocation();

    /// <summary>The method name of a call: <c>MapDelete</c> in <c>app.MapDelete("/", ...)</c>.</summary>
    public static Location Name(IInvocationOperation call) =>
        call.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
            ? member.Name.GetLocation()
            : call.Syntax.GetLocation();

    /// <summary>
    /// True inside <c>if (env.IsDevelopment())</c> or <c>if (env.IsEnvironment("Development"))</c>,
    /// or inside the <c>else</c> of <c>if (!env.IsDevelopment())</c>.
    /// </summary>
    public static bool InDevelopmentBranch(SyntaxNode node)
    {
        foreach (var statement in node.Ancestors().OfType<IfStatementSyntax>())
        {
            var condition = Unparenthesize(statement.Condition);
            if (condition is PrefixUnaryExpressionSyntax { Operand: var operand } negation &&
                negation.IsKind(SyntaxKind.LogicalNotExpression) &&
                IsDevelopmentCheck(Unparenthesize(operand)))
            {
                if (statement.Else?.Span.Contains(node.Span) == true)
                {
                    return true;
                }
            }
            else if (statement.Statement.Span.Contains(node.Span) &&
                     condition.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                         .Any(c => IsDevelopmentCheck(c) && !c.Parent.IsKind(SyntaxKind.LogicalNotExpression)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDevelopmentCheck(ExpressionSyntax expression) =>
        expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } } call &&
        (name == "IsDevelopment" ||
         (name == "IsEnvironment" && call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { Token.ValueText: "Development" }));

    private static ExpressionSyntax Unparenthesize(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    public static bool Implements(ITypeSymbol? type, INamedTypeSymbol? @interface) =>
        type is not null && @interface is not null &&
        (SymbolEqualityComparer.Default.Equals(type, @interface) ||
         type.AllInterfaces.Contains(@interface, SymbolEqualityComparer.Default));

    public static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol? baseType)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, baseType))
            {
                return true;
            }
        }

        return false;
    }
}
