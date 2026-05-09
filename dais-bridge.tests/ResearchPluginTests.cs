using Darbee.Gateway.Plugins;

namespace Darbee.Gateway.Tests;

public class ResearchPluginTests
{
    [Fact]
    public async Task QueryDocumentation_WhenLibraryIdResolved_CallsQueryDocsWithParsedId()
    {
        // Arrange
        var fake = new FakeMcpToolClient()
            .SetResponse("resolve-library-id", "Best match: Astro\nContext7-compatible library ID: /astro/docs\nTrust score: 9.5")
            .SetResponse("query-docs", "Astro is a static site generator. Use `npm run dev` to start.");
        var plugin = new ResearchPlugin(fake);

        // Act
        var result = await plugin.QueryDocumentation("Astro", "How do I start the dev server?");

        // Assert
        Assert.Equal("Astro is a static site generator. Use `npm run dev` to start.", result);
        Assert.Equal(2, fake.Calls.Count);
        Assert.Equal("resolve-library-id", fake.Calls[0].ToolName);
        Assert.Equal("query-docs", fake.Calls[1].ToolName);
        Assert.Equal("/astro/docs", fake.Calls[1].Arguments["libraryId"]);
        Assert.Equal("How do I start the dev server?", fake.Calls[1].Arguments["query"]);
    }

    [Fact]
    public async Task QueryDocumentation_WhenLibraryIdNotFound_ReturnsCannotResolveAndDoesNotCallQueryDocs()
    {
        // Arrange
        var fake = new FakeMcpToolClient()
            .SetResponse("resolve-library-id", "No matches found for the requested library.");
        var plugin = new ResearchPlugin(fake);

        // Act
        var result = await plugin.QueryDocumentation("NonexistentLib", "How does it work?");

        // Assert
        Assert.StartsWith("Could not resolve library ID for NonexistentLib.", result);
        Assert.Single(fake.Calls);
        Assert.Equal("resolve-library-id", fake.Calls[0].ToolName);
        Assert.DoesNotContain(fake.Calls, c => c.ToolName == "query-docs");
    }

    [Fact]
    public async Task QueryDocumentation_WhenQueryDocsReturnsEmpty_ReturnsNoDocumentationFound()
    {
        // Arrange
        var fake = new FakeMcpToolClient()
            .SetResponse("resolve-library-id", "Context7-compatible library ID: /astro/docs")
            .SetResponse("query-docs", string.Empty);
        var plugin = new ResearchPlugin(fake);

        // Act
        var result = await plugin.QueryDocumentation("Astro", "Anything?");

        // Assert
        Assert.Equal("No documentation found.", result);
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public async Task QueryDocumentation_WhenLibraryNameEmpty_ReturnsErrorAndMakesNoCalls()
    {
        // Arrange
        var fake = new FakeMcpToolClient();
        var plugin = new ResearchPlugin(fake);

        // Act
        var result = await plugin.QueryDocumentation("   ", "any query");

        // Assert
        Assert.Contains("libraryName is required", result);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task QueryDocumentation_WhenLibraryNameTooLong_ReturnsErrorAndMakesNoCalls()
    {
        // Arrange
        var fake = new FakeMcpToolClient();
        var plugin = new ResearchPlugin(fake);
        var oversized = new string('x', 201);

        // Act
        var result = await plugin.QueryDocumentation(oversized, "valid query");

        // Assert
        Assert.Contains("200 characters or fewer", result);
        Assert.Empty(fake.Calls);
    }
}
