using Darbee.Gateway.Domain.ValueObjects;

namespace Darbee.Gateway.Domain.Models;

public sealed class PostSearchHit
{
	public string Key { get; init; } = "";
	public string Slug { get; init; } = "";
	public string Collection { get; init; } = "";
	public string VectorKind { get; init; } = "";
	public string? Kind { get; init; }
	public TenantId? TenantId { get; init; }
	public string Title { get; init; } = "";
	public string Text { get; init; } = "";
	public string Description { get; init; } = "";
	public string? AiSummary { get; init; }
	public string? PubDate { get; init; }
	public string? Category { get; init; }
	public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
	public double Sim { get; init; }
}
