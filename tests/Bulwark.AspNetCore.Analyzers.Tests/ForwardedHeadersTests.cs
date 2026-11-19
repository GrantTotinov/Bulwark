namespace Bulwark.AspNetCore.Analyzers.Tests;

public class ForwardedHeadersTests
{
    [Fact]
    public async Task Reports_only_when_both_lists_of_one_options_object_are_cleared() =>
        Assert.Equal(["BWK011: o.KnownProxies.Clear()"], await Analyze.Program("""
            using Microsoft.AspNetCore.HttpOverrides;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });

            var a = new ForwardedHeadersOptions();
            a.KnownProxies.Clear(); // the loopback network still restricts who may forward
            var b = new ForwardedHeadersOptions();
            b.KnownIPNetworks.Clear();
            """));
}
