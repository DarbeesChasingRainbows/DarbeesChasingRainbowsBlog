using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace Darbee.Gateway.Plugins;

/// <summary>
/// Semantic Kernel plugin that lets an LLM look up live library/framework documentation
/// via an MCP tool client (e.g., Context7). All MCP wire concerns are delegated to
/// <see cref="IMcpToolClient"/>; this class owns only the orchestration:
/// (1) call <c>resolve-library-id</c>, (2) parse the canonical library ID,
/// (3) call <c>query-docs</c>, (4) return the first text block.
/// </summary>
public partial class ResearchPlugin
{
    private const int MaxArgLength = 200;

    private readonly IMcpToolClient _client;

    public ResearchPlugin(IMcpToolClient client)
    {
        _client = client;
    }

    [GeneratedRegex(@"Context7-compatible library ID:\s+(/[\w.\-]+/[\w.\-]+)")]
    private static partial Regex LibraryIdRegex();

    [KernelFunction, Description("Queries live documentation from Context7 via MCP.")]
    public async Task<string> QueryDocumentation(
        [Description("The name of the library or framework to search for (e.g., 'Astro', 'Semantic Kernel').")] string libraryName,
        [Description("The specific question or technical detail to research.")] string query)
    {
        // Input validation: return graceful strings (do NOT throw) so the LLM can recover
        // and the SK function-calling loop can continue.
        libraryName = (libraryName ?? string.Empty).Trim();
        query = (query ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(libraryName))
        {
            return "Could not resolve library ID: libraryName is required.";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return "Could not resolve library ID: query is required.";
        }

        if (libraryName.Length > MaxArgLength || query.Length > MaxArgLength)
        {
            return $"Could not resolve library ID: arguments must be {MaxArgLength} characters or fewer.";
        }

        try
        {
            // 1. Resolve Library ID
            var resolveText = await _client.CallToolAsync(
                "resolve-library-id",
                new Dictionary<string, object?>
                {
                    ["libraryName"] = libraryName,
                    ["query"] = query
                });

            var match = LibraryIdRegex().Match(resolveText);
            if (!match.Success)
            {
                return $"Could not resolve library ID for {libraryName}. Result: {resolveText}";
            }

            var libraryId = match.Groups[1].Value;

            // 2. Query Documentation
            var docText = await _client.CallToolAsync(
                "query-docs",
                new Dictionary<string, object?>
                {
                    ["libraryId"] = libraryId,
                    ["query"] = query
                });

            return string.IsNullOrEmpty(docText) ? "No documentation found." : docText;
        }
        catch (Exception ex)
        {
            // Swallow any MCP/transport exception and return a graceful string.
            // The plugin is invoked by the LLM via SK; throwing would kill the function-calling
            // loop. Timeout/retry/circuit-breaker hardening is tracked as plan Task 6 follow-up.
            return $"Research unavailable: {ex.Message}";
        }
    }
}
