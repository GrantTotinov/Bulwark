namespace Bulwark.AspNetCore.Analyzers.Tests;

public class CodeFixTests
{
    [Fact]
    public async Task Moves_the_rate_limiter_after_routing_and_keeps_blank_lines_in_place() =>
        Assert.Equal("""
            var app = WebApplication.Create(args);

            app.UseRouting();
            // per-endpoint limits
            app.UseRateLimiter();
            app.MapPost("/login", () => Results.Ok()).RequireRateLimiting("login");

            """, await Analyze.Fix("""
            var app = WebApplication.Create(args);

            // per-endpoint limits
            app.UseRateLimiter();
            app.UseRouting();
            app.MapPost("/login", () => Results.Ok()).RequireRateLimiting("login");

            """, "BWK002"));

    [Fact]
    public async Task Moves_the_rate_limiter_inside_a_method() =>
        Assert.Equal("""
            var app = WebApplication.Create(args);
            Configure(app);
            app.MapPost("/login", () => Results.Ok()).RequireRateLimiting("login");

            static void Configure(IApplicationBuilder app)
            {
                app.UseHttpsRedirection();
                app.UseRouting();
                app.UseRateLimiter();
            }

            """, await Analyze.Fix("""
            var app = WebApplication.Create(args);
            Configure(app);
            app.MapPost("/login", () => Results.Ok()).RequireRateLimiting("login");

            static void Configure(IApplicationBuilder app)
            {
                app.UseRateLimiter();
                app.UseHttpsRedirection();
                app.UseRouting();
            }

            """, "BWK002"));

    [Fact]
    public async Task Adds_validate_antiforgery_token_next_to_the_verb() =>
        Assert.Equal("""
            using Microsoft.AspNetCore.Mvc;

            var app = WebApplication.Create(args);

            public class C : Controller
            {
                [HttpPost("email"), ValidateAntiForgeryToken]
                public IActionResult ChangeEmail([FromForm] string email) => Ok();
            }

            """, await Analyze.Fix("""
            using Microsoft.AspNetCore.Mvc;

            var app = WebApplication.Create(args);

            public class C : Controller
            {
                [HttpPost("email")]
                public IActionResult ChangeEmail([FromForm] string email) => Ok();
            }

            """, "BWK004"));

    [Fact]
    public async Task Removes_insecure_settings_from_initializers_and_statements() =>
        Assert.Equal("""
            using Microsoft.IdentityModel.Tokens;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidAudience = "api",
                };
            });

            """, await Analyze.Fix(await Analyze.Fix("""
            using Microsoft.IdentityModel.Tokens;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(o =>
            {
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = false,
                    ValidAudience = "api",
                };
            });

            """, "BWK006") ?? "", "BWK005"));

    [Fact]
    public async Task Removes_the_developer_exception_page() =>
        Assert.Equal("""
            var app = WebApplication.Create(args);
            app.UseHttpsRedirection();

            """, await Analyze.Fix("""
            var app = WebApplication.Create(args);
            app.UseDeveloperExceptionPage();
            app.UseHttpsRedirection();

            """, "BWK010"));

    [Fact]
    public async Task Offers_no_removal_when_the_setting_is_a_lambda_body() =>
        Assert.Null(await Analyze.Fix("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(o => o.RequireHttpsMetadata = false);
            """, "BWK006"));

    [Fact]
    public async Task Requires_authorization_on_the_mcp_endpoint() =>
        Assert.Equal("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddMcpServer().WithHttpTransport();
            var app = builder.Build();
            app.MapMcp("/mcp").RequireAuthorization();

            """, await Analyze.Fix("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddMcpServer().WithHttpTransport();
            var app = builder.Build();
            app.MapMcp("/mcp");

            """, "BWK007"));

    [Fact]
    public async Task Makes_cookies_secure() =>
        Assert.Equal("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSession(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);

            """, await Analyze.Fix("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSession(o => o.Cookie.SecurePolicy = CookieSecurePolicy.None);

            """, "BWK008"));
}
