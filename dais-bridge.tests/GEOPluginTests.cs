using System.Threading.Tasks;
using DAIS.Bridge.Plugins;
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

        // Act & Assert
        // (Just compilation check for now)
        await Task.CompletedTask;
    }
}
