namespace Bulwark.AspNetCore.Analyzers.Tests;

public class CorsTests
{
    [Fact]
    public async Task Reports_an_always_true_origin_check_with_credentials_set_in_separate_statements() =>
        Assert.Equal(["BWK001: SetIsOriginAllowed(origin => { return true; })"], await Analyze.Program("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddCors(o => o.AddPolicy("spa", policy =>
            {
                policy.SetIsOriginAllowed(origin => { return true; });
                policy.AllowCredentials();
            }));
            """));

    [Fact]
    public async Task Reports_any_origin_and_wildcard_origins_with_credentials() =>
        Assert.Equal(["BWK001: AllowAnyOrigin()", "BWK001: WithOrigins(\"*\")"], await Analyze.Program("""
            using Microsoft.AspNetCore.Cors.Infrastructure;

            var a = new CorsPolicyBuilder().AllowAnyOrigin().AllowCredentials().Build();
            var b = new CorsPolicyBuilder().WithOrigins("*").AllowCredentials().Build();
            """));

    [Fact]
    public async Task Ignores_explicit_origins_with_credentials_and_any_origin_without_credentials() =>
        Assert.Empty(await Analyze.Program("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddCors(o =>
            {
                o.AddPolicy("app", p => p.WithOrigins("https://app.example.com").AllowCredentials());
                o.AddPolicy("public", p => p.SetIsOriginAllowed(_ => true));
                o.AddPolicy("checked", p => p.SetIsOriginAllowed(origin => origin == "https://app.example.com").AllowCredentials());
            });
            """));
}
