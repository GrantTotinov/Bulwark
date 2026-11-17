namespace Bulwark.AspNetCore.Analyzers.Tests;

public class RateLimiterOrderTests
{
    [Fact]
    public async Task Reports_a_chained_call_before_routing_when_an_attribute_names_a_policy() =>
        Assert.Equal(["BWK002: UseRateLimiter()"], await Analyze.Program("""
            using Microsoft.AspNetCore.RateLimiting;

            var app = WebApplication.Create(args);
            app.UseRateLimiter().UseRouting();
            app.MapPost("/login", [EnableRateLimiting("login")] () => Results.Ok());
            """));

    [Fact]
    public async Task Ignores_the_limiter_after_routing_or_without_explicit_routing() =>
        Assert.Empty(await Analyze.Program("""
            var app = WebApplication.Create(args);
            app.UseRouting();
            app.UseRateLimiter();
            app.MapPost("/login", () => Results.Ok()).RequireRateLimiting("login");

            var other = WebApplication.Create(args);
            other.UseRateLimiter(); // WebApplication adds UseRouting() at the start of the pipeline
            other.MapPost("/login", () => Results.Ok()).RequireRateLimiting("login");
            """));

    [Fact]
    public async Task Ignores_a_global_limiter_before_routing() =>
        Assert.Empty(await Analyze.Program("""
            var app = WebApplication.Create(args);
            app.UseRateLimiter(); // only a GlobalLimiter, which the docs allow before routing
            app.UseRouting();
            """));

    [Fact]
    public async Task Ignores_routing_on_a_different_pipeline() =>
        Assert.Empty(await Analyze.Program("""
            var app = WebApplication.Create(args);
            app.UseRouting();
            app.UseRateLimiter();
            app.UseWhen(_ => true, branch => branch.UseRouting());
            app.MapPost("/login", () => Results.Ok()).RequireRateLimiting("login");
            """));
}
