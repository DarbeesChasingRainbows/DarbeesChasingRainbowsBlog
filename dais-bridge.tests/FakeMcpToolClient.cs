using Darbee.Gateway.Plugins;

namespace Darbee.Gateway.Tests;

/// <summary>
/// Hand-rolled test double for <see cref="IMcpToolClient"/>. Records each invocation
/// and returns canned responses keyed by tool name. Kept dependency-free (no Moq).
/// </summary>
internal sealed class FakeMcpToolClient : IMcpToolClient
{
    private readonly Dictionary<string, string> _responses = new(StringComparer.Ordinal);

    public List<Invocation> Calls { get; } = new();

    public FakeMcpToolClient SetResponse(string toolName, string text)
    {
        _responses[toolName] = text;
        return this;
    }

    public Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new Invocation(toolName, new Dictionary<string, object?>(arguments)));
        return Task.FromResult(_responses.TryGetValue(toolName, out var canned) ? canned : string.Empty);
    }

    internal sealed record Invocation(string ToolName, IReadOnlyDictionary<string, object?> Arguments);
}
