using System.Threading.Tasks;
using Darbee.Gateway.Plugins;
using Xunit;

namespace DAIS.Bridge.Tests;

public class GEOPluginTests
{
    [Fact]
    public async Task GenerateMetadata_ShouldReturnJson()
    {
        // Arrange
        var plugin = new GEOPlugin("http://localhost:1234/v1", "local-model");
        var content = "This is a test blog post.";

        // Act
        var result = await plugin.GenerateMetadata(content);

        // Assert
        Assert.Contains("summary", result);
    }
}
