namespace Darbee.Gateway.Memory.Models;

public sealed record PostDocument(
    string Collection,
    string Slug,
    string Title,
    string Description,
    string Body,
    string? AiSummary,
    IReadOnlyList<string> KeyTakeaways,
    IReadOnlyList<FaqEntry> Faq,
    IReadOnlyList<string> EntityMentions,
    IReadOnlyList<string> Tags,
    string? Category,
    string? PubDate);
