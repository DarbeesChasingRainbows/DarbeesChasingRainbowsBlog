namespace Darbee.Gateway.Memory.Models;

public sealed record UpsertNoteResult(
    string Key,
    VectorWriteOutcome Outcome,
    string? Reason = null);
