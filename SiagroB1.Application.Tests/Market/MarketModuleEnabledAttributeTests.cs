using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra;
using SiagroB1.Web.Filters;

namespace SiagroB1.Application.Tests.Market;

public class MarketModuleEnabledAttributeTests
{
    private static ResourceExecutingContext Context(IConfiguration configuration)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(configuration).BuildServiceProvider(),
        };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(action, [], []);
    }

    [Fact]
    public void IsResourceFilter_SoItRunsBeforeModelBinding() =>
        Assert.IsAssignableFrom<IAsyncResourceFilter>(new MarketModuleEnabledAttribute());

    [Fact]
    public async Task Disabled_Returns404_WithMarker()
    {
        var context = Context(TestConfiguration.Empty());
        var nextCalled = false;

        await new MarketModuleEnabledAttribute().OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ResourceExecutedContext>(null!);
        });

        Assert.False(nextCalled);
        var result = Assert.IsType<ContentResult>(context.Result);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("application/json", result.ContentType);
        Assert.Equal("{\"code\":\"MarketModuleDisabled\"}", result.Content);
        Assert.Equal("disabled", context.HttpContext.Response.Headers[MarketModuleEnabledAttribute.HeaderName]);
    }

    [Fact]
    public async Task Enabled_CallsNext()
    {
        var context = Context(TestConfiguration.With((MarketModule.ConfigKey, "true")));
        var nextCalled = false;

        await new MarketModuleEnabledAttribute().OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ResourceExecutedContext>(null!);
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }
}
