using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Collections.Generic;
using System.Linq;

namespace Darbee.Gateway.Plugins;

public class ResearchPlugin
{
    private readonly string _endpoint;

    public ResearchPlugin(string endpoint)
    {
        _endpoint = endpoint;
    }

    [KernelFunction, Description("Queries live documentation from Context7 via MCP.")]
    public async Task<string> QueryDocumentation(
        [Description("The name of the library or framework to search for (e.g., 'Astro', 'Semantic Kernel').")] string libraryName,
        [Description("The specific question or technical detail to research.")] string query)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new System.Uri(_endpoint),
            TransportMode = HttpTransportMode.StreamableHttp
        });

        await using var client = await McpClient.CreateAsync(transport);

        // 1. Resolve Library ID
        var resolveResult = await client.CallToolAsync(
            "resolve-library-id",
            new Dictionary<string, object?>
            {
                ["libraryName"] = libraryName,
                ["query"] = query
            });

        // Simplified logic: assume first match is best for this prototype
        // In a real scenario, we would parse the result more carefully
        var resolveText = resolveResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
        
        // Extract Library ID (this is a placeholder for actual parsing)
        // Expected format contains "Context7-compatible library ID: /org/project"
        var match = System.Text.RegularExpressions.Regex.Match(resolveText, @"library ID: (/\S+)");
        if (!match.Success)
        {
            return $"Could not resolve library ID for {libraryName}. Result: {resolveText}";
        }

        var libraryId = match.Groups[1].Value;

        // 2. Query Documentation
        var docResult = await client.CallToolAsync(
            "query-docs",
            new Dictionary<string, object?>
            {
                ["libraryId"] = libraryId,
                ["query"] = query
            });

        return docResult.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No documentation found.";
    }
}
