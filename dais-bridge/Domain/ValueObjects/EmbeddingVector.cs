namespace Darbee.Gateway.Domain.ValueObjects;

/// <summary>
/// Value Object wrapping an embedding float array.
/// Validates dimension at construction to catch mismatches early.
/// </summary>
public sealed class EmbeddingVector : IEquatable<EmbeddingVector>
{
    public float[] Values { get; }
    public int Dimension => Values.Length;

    public EmbeddingVector(float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
            throw new ArgumentException("Embedding vector must not be empty.", nameof(values));
        Values = values;
    }

    public EmbeddingVector(float[] values, int expectedDimension)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != expectedDimension)
            throw new ArgumentException(
                $"Embedding dimension mismatch: expected {expectedDimension}, got {values.Length}.",
                nameof(values));
        Values = values;
    }

    public static implicit operator float[](EmbeddingVector v) => v.Values;

    public bool Equals(EmbeddingVector? other)
    {
        if (other is null) return false;
        return Values.AsSpan().SequenceEqual(other.Values.AsSpan());
    }

    public override bool Equals(object? obj) => Equals(obj as EmbeddingVector);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var f in Values.AsSpan()[..Math.Min(Values.Length, 8)])
            hash.Add(f);
        return hash.ToHashCode();
    }
}
