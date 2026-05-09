namespace Darbee.Gateway.Plugins;

/// <summary>
/// Abstraction over an MCP tool-calling client. Returns the first text content block
/// produced by the tool, or an empty string if the tool returned no text content.
/// </summary>
/// <remarks>
/// This interface isolates the MCP wire protocol from plugin orchestration so that
/// plugins (e.g., <see cref="ResearchPlugin"/>) can be unit-tested without an MCP server.
/// Production wiring is provided by <see cref="Context7McpToolClient"/>.
/// </remarks>
public interface IMcpToolClient
{
    Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}
