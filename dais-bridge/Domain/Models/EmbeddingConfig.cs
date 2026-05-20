namespace Darbee.Gateway.Domain.Models;

/// <summary>
/// Value Object representing the embedding model configuration.
/// Promoted from a plain record: validates dimension > 0 and model non-empty.
/// </summary>
public sealed record EmbeddingConfig
{
    public string Model { get; }
    public int Dimension { get; }

    public EmbeddingConfig(string model, int dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model, nameof(model));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimension, 0, nameof(dimension));
        Model = model;
        Dimension = dimension;
    }
}
