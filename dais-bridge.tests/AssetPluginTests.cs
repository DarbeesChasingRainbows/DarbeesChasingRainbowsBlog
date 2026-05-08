using System.Threading.Tasks;
using Darbee.Gateway.Plugins;
using Xunit;

namespace DAIS.Bridge.Tests;

public class AssetPluginTests
{
    [Fact]
    public async Task WashImage_ShouldReturnCloudUrl()
    {
        // Arrange
        var accountId = "accountId";
        var plugin = new AssetPlugin(accountId, "apiToken");
        var localPath = "test.jpg";

        // Act
        var result = await plugin.WashImage(localPath);

        // Assert
        Assert.Contains(accountId, result);
    }
}
