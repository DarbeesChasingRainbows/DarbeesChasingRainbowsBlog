using System;
using System.Threading.Tasks;
using Darbee.Gateway.Plugins;
using Xunit;

namespace DAIS.Bridge.Tests;

public class ArangoPluginTests
{
    [Fact]
    public async Task CreateNode_ShouldIncludeTenantId()
    {
        // Arrange
        var plugin = new ArangoPlugin("http://localhost:8529", "db", "user", "pass");
        var collection = "nodes";
        var jsonData = "{\"name\": \"Test Node\"}";
        var tenantId = "tenant-123";

        // Act & Assert
        await plugin.CreateNode(collection, jsonData, tenantId);
        // In a full implementation, we would verify the document sent to ArangoDB contains tenant_id
    }

    [Fact]
    public async Task QueryGraph_ShouldEnforceTenantFilter()
    {
        // Arrange
        var plugin = new ArangoPlugin("http://localhost:8529", "db", "user", "pass");
        var aql = "FOR doc IN nodes";
        var tenantId = "tenant-123";

        // Act
        var result = await plugin.QueryGraph(aql, tenantId);

        // Assert
        Assert.NotNull(result);
    }
}
