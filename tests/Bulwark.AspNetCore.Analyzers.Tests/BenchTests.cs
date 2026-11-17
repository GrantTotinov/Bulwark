namespace Bulwark.AspNetCore.Analyzers.Tests;

/// <summary>
/// The pairs from aspnetcore-security-bench (see bench/README.md). A rule detects a pair when it reports
/// the vulnerable program and reports nothing on the fixed one.
/// </summary>
public class BenchTests
{
    [Theory]
    [InlineData("R01-method-override-antiforgery", "BWK003: MapDelete")]
    [InlineData("R02-rate-limiter-before-routing", "BWK002: UseRateLimiter()")]
    [InlineData("R03-mvc-missing-antiforgery", "BWK004: ChangeEmail")]
    [InlineData("R07-cors-any-origin-credentials", "BWK001: SetIsOriginAllowed(_ => true)")]
    public async Task Detects_the_vulnerable_program_and_not_the_fixed_one(string pair, string expected)
    {
        Assert.Equal([expected], await Analyze.Program(Read(pair, "Vulnerable")));
        Assert.Empty(await Analyze.Program(Read(pair, "Fixed")));
    }

    private static string Read(string pair, string variant) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bench", pair, variant + ".cs"));
}
