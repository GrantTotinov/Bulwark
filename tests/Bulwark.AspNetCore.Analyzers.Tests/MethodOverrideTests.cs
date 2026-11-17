namespace Bulwark.AspNetCore.Analyzers.Tests;

public class MethodOverrideTests
{
    private const string Pipeline = """
        using Microsoft.AspNetCore.Antiforgery;
        using Microsoft.AspNetCore.Mvc;

        var app = WebApplication.Create(args);
        var options = new HttpMethodOverrideOptions();
        options.FormFieldName = "_method";
        app.UseHttpMethodOverride(options);
        app.UseRouting();
        app.UseAntiforgery();

        """;

    [Fact]
    public async Task Reports_form_file_and_method_group_handlers() =>
        Assert.Equal(["BWK003: MapDelete", "BWK003: MapDelete"], await Analyze.Program(Pipeline + """
            app.MapDelete("/files/{id}", (int id, IFormFile reason) => Results.Ok());
            app.MapDelete("/items/{id}", Handlers.Delete);

            static class Handlers
            {
                public static IResult Delete(int id, [FromForm] string note) => Results.Ok();
            }
            """));

    [Fact]
    public async Task Ignores_endpoints_that_validate_the_token_disable_antiforgery_or_bind_no_form() =>
        Assert.Empty(await Analyze.Program(Pipeline + """
            app.MapDelete("/a/{id}", async (int id, [FromForm] string note, IAntiforgery antiforgery, HttpContext context) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                return Results.Ok();
            });
            app.MapDelete("/b/{id}", (int id, [FromForm] string note) => Results.Ok()).DisableAntiforgery();
            app.MapDelete("/c/{id}", (int id) => Results.Ok());
            """));

    [Fact]
    public async Task Ignores_form_bound_delete_without_form_field_override() =>
        Assert.Empty(await Analyze.Program("""
            using Microsoft.AspNetCore.Mvc;

            var app = WebApplication.Create(args);
            app.UseHttpMethodOverride(); // X-HTTP-Method-Override header only, which a cross-site form cannot set
            app.MapDelete("/items/{id}", (int id, [FromForm] string note) => Results.Ok());
            """));
}
