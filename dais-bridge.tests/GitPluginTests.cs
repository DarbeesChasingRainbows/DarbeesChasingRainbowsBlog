using System.Threading.Tasks;
using DAIS.Bridge.Plugins;
using Xunit;

namespace DAIS.Bridge.Tests;

public class GitPluginTests
{
    [Fact]
    public async Task StageAndCommit_ShouldNotThrow()
    {
        // Arrange
        var plugin = new GitPlugin();
        var filePath = "non-existent-test.txt";
        var message = "DAIS: Test commit";

        // Act & Assert
        // We test that it doesn't throw, even if git fails on a non-existent file
        // in this test environment.
        await plugin.StageAndCommit(filePath, message);
    }
}
