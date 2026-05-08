using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Xunit;
using Darbee.Gateway.Middleware;

namespace Darbee.Gateway.Tests;

public class SafetyMiddlewareTests
{
    [Fact]
    public async Task Middleware_ShouldBlockUnsafeContent()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var content = "This request mentions guns and violence.";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        context.Request.Body = stream;
        context.Request.ContentLength = stream.Length;
        
        // Mock Response.Body to capture output
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        var middleware = new SafetyMiddleware(async (innerContext) => 
        {
            innerContext.Response.StatusCode = 200;
            await innerContext.Response.WriteAsync("Success");
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        
        responseStream.Position = 0;
        var responseBody = await new StreamReader(responseStream).ReadToEndAsync();
        Assert.Contains("Policy Violation", responseBody);
    }

    [Fact]
    public async Task Middleware_ShouldAllowSafeContent()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var content = "This request is safe and contains rainbows.";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        context.Request.Body = stream;
        context.Request.ContentLength = stream.Length;

        // Mock Response.Body to capture output
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        var middleware = new SafetyMiddleware(async (innerContext) => 
        {
            innerContext.Response.StatusCode = 200;
            await innerContext.Response.WriteAsync("Success");
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
        
        responseStream.Position = 0;
        var responseBody = await new StreamReader(responseStream).ReadToEndAsync();
        Assert.Equal("Success", responseBody);
    }
}
