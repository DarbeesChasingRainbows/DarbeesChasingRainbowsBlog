using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Darbee.Gateway.Plugins;

/// <summary>
/// Production <see cref="IMcpToolClient"/> implementation backed by an HTTP MCP transport
/// (Context7 or any other compatible streamable-HTTP MCP server).
/// </summary>
/// <remarks>
/// This is the only place in the production codebase that touches
/// <c>ModelContextProtocol.*</c> types. Per-call client lifecycle is intentional for
/// the initial design; connection pooling is deferred (see plan Task 6).
/// </remarks>
public sealed class Context7McpToolClient : IMcpToolClient
{
    private readonly Uri _endpoint;

    public Context7McpToolClient(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint must not be null or whitespace.", nameof(endpoint));
        }

        _endpoint = new Uri(endpoint);
    }

    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = _endpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        });

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

        return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
    }
}
