using System.Text;
using Darbee.Gateway.Domain.Models;

namespace Darbee.Gateway.Domain.Services;

public static class PostTextComposer
{
    public static string ComposeSummary(PostDocument post)
    {
        var sb = new StringBuilder();
        AppendIfNotEmpty(sb, post.Title);
        AppendIfNotEmpty(sb, post.Description);
        if (!string.IsNullOrWhiteSpace(post.AiSummary))
            AppendIfNotEmpty(sb, $"AI Summary: {post.AiSummary}");

        if (post.KeyTakeaways is { Count: > 0 })
        {
            sb.AppendLine("Key Takeaways:");
            foreach (var t in post.KeyTakeaways)
                sb.AppendLine($"- {t}");
            sb.AppendLine();
        }

        if (post.Faq is { Count: > 0 })
        {
            sb.AppendLine("FAQ:");
            foreach (var f in post.Faq)
            {
                sb.AppendLine($"Q: {f.Question}");
                sb.AppendLine($"A: {f.Answer}");
                sb.AppendLine();
            }
        }

        if (post.EntityMentions is { Count: > 0 })
            AppendIfNotEmpty(sb, $"Mentions: {string.Join(", ", post.EntityMentions)}");

        return sb.ToString().TrimEnd() + "\n";
    }

    public static string ComposeBody(PostDocument post)
    {
        var sb = new StringBuilder();
        AppendIfNotEmpty(sb, post.Title);
        AppendIfNotEmpty(sb, post.Description);
        if (post.Tags is { Count: > 0 })
            AppendIfNotEmpty(sb, $"Tags: {string.Join(", ", post.Tags)}");
        if (!string.IsNullOrWhiteSpace(post.Category))
            AppendIfNotEmpty(sb, $"Category: {post.Category}");
        if (post.EntityMentions is { Count: > 0 })
            AppendIfNotEmpty(sb, $"Mentions: {string.Join(", ", post.EntityMentions)}");
        AppendIfNotEmpty(sb, post.Body);
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendIfNotEmpty(StringBuilder sb, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        sb.AppendLine(line);
        sb.AppendLine();
    }
}
