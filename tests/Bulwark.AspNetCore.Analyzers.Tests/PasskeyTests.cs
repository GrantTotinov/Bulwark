namespace Bulwark.AspNetCore.Analyzers.Tests;

public class PasskeyTests
{
    private const string Endpoints = """
        app.MapPost("/passkey/request", (SignInManager<IdentityUser> signIn) => signIn.MakePasskeyRequestOptionsAsync(null));
        app.MapPost("/passkey/create", (SignInManager<IdentityUser> signIn) =>
            signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = "1", Name = "a", DisplayName = "a" }));
        """;

    [Fact]
    public async Task Reports_passkey_options_without_a_server_domain() =>
        Assert.Equal(["BWK012: MakePasskeyRequestOptionsAsync", "BWK012: MakePasskeyCreationOptionsAsync"], await Analyze.Program($$"""
            using Microsoft.AspNetCore.Identity;

            var app = WebApplication.Create(args);
            {{Endpoints}}
            """));

    [Fact]
    public async Task Ignores_passkeys_with_a_server_domain() =>
        Assert.Empty(await Analyze.Program($$"""
            using Microsoft.AspNetCore.Identity;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.Configure<IdentityPasskeyOptions>(o => o.ServerDomain = "example.com");
            var app = builder.Build();
            {{Endpoints}}
            """));
}
