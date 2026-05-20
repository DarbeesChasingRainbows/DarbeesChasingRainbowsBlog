namespace Darbee.Gateway.Domain.Models;

public sealed record UpsertNoteResult(
    string Key,
    VectorWriteOutcome Outcome,
    string? Reason = null);
