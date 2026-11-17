namespace Bulwark.AspNetCore.Analyzers.Tests;

public class MvcAntiforgeryTests
{
    [Fact]
    public async Task Reports_each_unsafe_action_with_its_verb()
    {
        var diagnostics = await Analyze.Program("""
            using Microsoft.AspNetCore.Mvc;

            var app = WebApplication.Create(args);

            public class OrdersController : Controller
            {
                public IActionResult Index() => View();
                [HttpGet] public IActionResult List() => View();
                [HttpPost] public IActionResult Create() => Ok();
                [HttpDelete("{id}")] public IActionResult Remove(int id) => Ok();
                [AcceptVerbs("GET", "PUT")] public IActionResult Replace() => Ok();
                [HttpPost, NonAction] public IActionResult Helper() => Ok();
                [HttpPost] private IActionResult Hidden() => Ok();
            }
            """);

        Assert.Equal(["BWK004: Create", "BWK004: Remove", "BWK004: Replace"], diagnostics);
    }

    [Theory]
    [InlineData("[ValidateAntiForgeryToken] public class C : Controller { [HttpPost] public IActionResult A() => Ok(); }")]
    [InlineData("public class C : Controller { [HttpPost, ValidateAntiForgeryToken] public IActionResult A() => Ok(); }")]
    [InlineData("public class C : Controller { [HttpPost, IgnoreAntiforgeryToken] public IActionResult A() => Ok(); }")]
    [InlineData("[ApiController] public class C : Controller { [HttpPost] public IActionResult A() => Ok(); }")]
    [InlineData("public class C : ControllerBase { [HttpPost] public IActionResult A() => Ok(); }")]
    [InlineData("[AutoValidateAntiforgeryToken] public class Base : Controller { } public class C : Base { [HttpPost] public IActionResult A() => Ok(); }")]
    public async Task Ignores_protected_opted_out_and_api_controllers(string controllers) =>
        Assert.Empty(await Analyze.Program($$"""
            using Microsoft.AspNetCore.Mvc;

            var app = WebApplication.Create(args);

            {{controllers}}
            """));

    [Theory]
    [InlineData("o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute())")]
    [InlineData("o.Filters.Add<AutoValidateAntiforgeryTokenAttribute>()")]
    [InlineData("o.Filters.Add(typeof(AutoValidateAntiforgeryTokenAttribute))")]
    public async Task Ignores_actions_when_a_global_filter_is_registered(string registration) =>
        Assert.Empty(await Analyze.Program($$"""
            using Microsoft.AspNetCore.Mvc;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllersWithViews(o => {{registration}});

            public class C : Controller { [HttpPost] public IActionResult A() => Ok(); }
            """));
}
