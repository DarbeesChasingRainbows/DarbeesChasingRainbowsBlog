namespace Darbee.Gateway.Memory.Models;

public enum VectorWriteOutcome
{
    Embedded,
    Cached,
    Failed,
}

public sealed record UpsertPostResult(
    string Slug,
    string Collection,
    VectorWriteOutcome Summary,
    VectorWriteOutcome Body,
    string? FailureReason = null);
