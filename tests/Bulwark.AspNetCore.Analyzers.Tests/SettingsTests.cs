namespace Bulwark.AspNetCore.Analyzers.Tests;

public class SettingsTests
{
    [Fact]
    public async Task Reports_disabled_signature_checks_and_http_metadata()
    {
        var diagnostics = await Analyze.Program("""
            using Microsoft.IdentityModel.JsonWebTokens;
            using Microsoft.IdentityModel.Tokens;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(o =>
            {
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = false,
                    SignatureValidator = (token, _) => new JsonWebToken(token),
                };
            });
            """);

        Assert.Equal(
            ["BWK006: o.RequireHttpsMetadata = false", "BWK005: RequireSignedTokens = false", "BWK005: SignatureValidator = (token, _) => new JsonWebToken(token)"],
            diagnostics);
    }

    [Fact]
    public async Task Reports_a_signature_validator_that_only_reads_the_token() =>
        Assert.Equal(["BWK005: parameters.SignatureValidator = delegate (string token, TokenValidationParameters p) { return new JsonWebTokenHandler().ReadJsonWebToken(token); }"],
            await Analyze.Program("""
                using Microsoft.IdentityModel.JsonWebTokens;
                using Microsoft.IdentityModel.Tokens;

                var parameters = new TokenValidationParameters();
                parameters.SignatureValidator = delegate (string token, TokenValidationParameters p) { return new JsonWebTokenHandler().ReadJsonWebToken(token); };
                """));

    [Fact]
    public async Task Ignores_development_only_http_metadata_and_validating_signature_validators() =>
        Assert.Empty(await Analyze.Program("""
            using Microsoft.IdentityModel.JsonWebTokens;
            using Microsoft.IdentityModel.Tokens;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(o =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    o.RequireHttpsMetadata = false;
                }

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = true,
                    SignatureValidator = (token, p) => Check(token, p),
                };
            }).AddJwtBearer("other", o => o.RequireHttpsMetadata = !builder.Environment.IsDevelopment());

            static SecurityToken Check(string token, TokenValidationParameters p) =>
                Validate(token) ? new JsonWebToken(token) : throw new SecurityTokenInvalidSignatureException();

            static bool Validate(string token) => token.Length > 0;
            """));

    [Fact]
    public async Task Reports_http_metadata_when_not_in_development() =>
        Assert.Equal(["BWK006: o.RequireHttpsMetadata = false"], await Analyze.Program("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(o =>
            {
                if (!builder.Environment.IsDevelopment())
                {
                    o.RequireHttpsMetadata = false;
                }
            });
            """));

    [Fact]
    public async Task Reports_cookies_without_secure_or_http_only() =>
        Assert.Equal(["BWK008: o.Cookie.SecurePolicy = CookieSecurePolicy.None", "BWK009: o.Cookie.HttpOnly = false"], await Analyze.Program("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddCookie(o =>
            {
                o.Cookie.SecurePolicy = CookieSecurePolicy.None;
                o.Cookie.HttpOnly = false;
            });
            builder.Services.AddSession(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
            builder.Services.AddAntiforgery(o => o.Cookie.HttpOnly = true);
            if (builder.Environment.IsDevelopment())
            {
                builder.Services.AddSession(o => o.Cookie.SecurePolicy = CookieSecurePolicy.None);
            }
            """));

    [Fact]
    public async Task Reports_error_details_outside_development()
    {
        var diagnostics = await Analyze.Program("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSignalR(o => o.EnableDetailedErrors = true);
            builder.Services.AddServerSideBlazor(o => o.DetailedErrors = true);
            builder.Services.AddRazorComponents().AddInteractiveServerComponents(o => o.DetailedErrors = builder.Environment.IsDevelopment());

            var app = builder.Build();
            app.UseDeveloperExceptionPage();
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/error");
            }
            else
            {
                app.UseDeveloperExceptionPage();
            }

            if (app.Environment.IsEnvironment("Development"))
            {
                app.UseDeveloperExceptionPage();
            }
            """);

        Assert.Equal(["BWK010: o.EnableDetailedErrors = true", "BWK010: o.DetailedErrors = true", "BWK010: UseDeveloperExceptionPage()"], diagnostics);
    }
}
