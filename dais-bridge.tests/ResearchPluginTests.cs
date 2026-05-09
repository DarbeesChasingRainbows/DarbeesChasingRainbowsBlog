using System.Threading.Tasks;
using Darbee.Gateway.Plugins;
using Xunit;

namespace DAIS.Bridge.Tests;

public class ResearchPluginTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithEndpoint()
    {
        // Arrange
        var endpoint = "http://localhost:3000/mcp";

        // Act
        var plugin = new ResearchPlugin(endpoint);

        // Assert
        Assert.NotNull(plugin);
    }

    [Fact]
    public async Task QueryDocumentation_ShouldHandleResolutionFailureGracefully()
    {
        // Note: Full integration testing would require a running MCP server.
        // For this scaffolding test, we verify that the logic doesn't crash 
        // if the network/endpoint is unreachable (it will throw, which is expected).
        
        var plugin = new ResearchPlugin("http://invalid-endpoint");
        
        await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(async () => 
            await plugin.QueryDocumentation("TestLib", "How to test?"));
    }
}
