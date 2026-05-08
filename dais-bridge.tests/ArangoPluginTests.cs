using System;
using System.Threading.Tasks;
using DAIS.Bridge.Plugins;
using Xunit;

namespace DAIS.Bridge.Tests;

public class ArangoPluginTests
{
    [Fact]
    public async Task CreateNode_ShouldSucceed()
    {
        // Arrange
        var plugin = new ArangoPlugin("http://localhost:8529", "db", "user", "pass");
        var collection = "nodes";
        var jsonData = "{\"name\": \"Test Node\"}";

        // Act & Assert (Should not throw compilation error in Green)
        await plugin.CreateNode(collection, jsonData);
    }
}
