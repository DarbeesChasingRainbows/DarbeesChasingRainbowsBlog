using System.Security.Cryptography;
using System.Text;

namespace Darbee.Gateway.Domain.ValueObjects;

/// <summary>
/// Value Object wrapping a content hash string (e.g. "sha256:abcdef...").
/// Encapsulates the ComputeHash logic that currently lives in MemoryStore.
/// </summary>
public readonly record struct ContentHash
{
    public string Value { get; }

    private ContentHash(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Compute a SHA-256 content hash from text and embedding model ID.
    /// </summary>
    public static ContentHash From(string text, string modelId)
    {
        var input = $"{modelId}\n{text}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return new ContentHash($"sha256:{hex}");
    }

    /// <summary>
    /// Compute a raw SHA-256 hex string (no prefix).
    /// </summary>
    public static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Compute a SHA-1 hex string — used for ArangoDB document keys.
    /// </summary>
    public static string Sha1Hex(string input)
    {
#pragma warning disable CA5350 // SHA1 used only for deterministic key generation, not security
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
#pragma warning restore CA5350
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Wrap an existing hash string (e.g. from database).
    /// </summary>
    public static ContentHash Parse(string hash) => new(hash);

    public override string ToString() => Value;

    public static implicit operator string(ContentHash hash) => hash.Value;
}
