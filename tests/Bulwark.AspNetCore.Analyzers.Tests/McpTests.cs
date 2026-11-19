namespace Bulwark.AspNetCore.Analyzers.Tests;

public class McpTests
{
    private const string Server = """
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddMcpServer().WithHttpTransport();
        var app = builder.Build();

        """;

    [Fact]
    public async Task Reports_only_the_endpoint_without_authorization() =>
        Assert.Equal(["BWK007: MapMcp"], await Analyze.Program(Server + """
            app.MapMcp();
            app.MapMcp("/a").RequireAuthorization();
            app.MapGroup("/b").RequireAuthorization().MapMcp();
            var group = app.MapGroup("/c");
            group.RequireAuthorization("mcp");
            group.MapMcp();
            var other = app.MapGroup("/d").RequireAuthorization();
            other.MapMcp();
            app.MapMcp("/public").AllowAnonymous();
            """));

    [Theory]
    [InlineData("builder.Services.AddAuthorization(o => o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());")]
    [InlineData("builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());")]
    [InlineData("builder.Services.AddMcpServer().WithHttpTransport().AddAuthorizationFilters();")]
    public async Task Ignores_endpoints_when_the_project_authorizes_globally(string setup) =>
        Assert.Empty(await Analyze.Program($$"""
            var builder = WebApplication.CreateBuilder(args);
            {{setup}}
            var app = builder.Build();
            app.MapMcp();
            """));
}
