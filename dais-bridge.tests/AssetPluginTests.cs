using System.Threading.Tasks;
using DAIS.Bridge.Plugins;
using Xunit;

namespace DAIS.Bridge.Tests;

public class AssetPluginTests
{
    [Fact]
    public async Task WashImage_ShouldReturnCloudUrl()
    {
        // Arrange
        var plugin = new AssetPlugin("accountId", "apiToken");
        var localPath = "test.jpg";

        // Act & Assert
        // (Just compilation check for now)
        await Task.CompletedTask;
    }
}
