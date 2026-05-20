namespace Darbee.Gateway.Domain.ValueObjects;

/// <summary>
/// Value Object wrapping a tenant identifier string.
/// Validates non-empty at construction — eliminates scattered ValidateTenantId() calls.
/// </summary>
public readonly record struct TenantId
{
    public string Value { get; }

    public TenantId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Value = value;
    }

    public override string ToString() => Value;

    public static implicit operator string(TenantId id) => id.Value;
}
