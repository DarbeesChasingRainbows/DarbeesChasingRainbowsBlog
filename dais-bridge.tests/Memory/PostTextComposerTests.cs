using Darbee.Gateway.Infrastructure.Arango;
using Darbee.Gateway.Infrastructure.Embedding;
using Darbee.Gateway.Domain.Exceptions;
using Darbee.Gateway.Domain.Models;
using Darbee.Gateway.Domain.Services;

namespace Darbee.Gateway.Tests.Memory;

public class PostTextComposerTests
{
    private static PostDocument SamplePost(
        string? aiSummary = "AI summary text",
        IReadOnlyList<string>? takeaways = null,
        IReadOnlyList<FaqEntry>? faq = null,
        IReadOnlyList<string>? mentions = null,
        string? category = "Faith",
        IReadOnlyList<string>? tags = null) =>
        new PostDocument(
            Collection: "blog",
            Slug: "welcome",
            Title: "Welcome",
            Description: "An intro post.",
            Body: "Hello from the road.",
            AiSummary: aiSummary,
            KeyTakeaways: takeaways ?? new[] { "One", "Two" },
            Faq: faq ?? new[] { new FaqEntry("Q1?", "A1.") },
            EntityMentions: mentions ?? new[] { "Kingdom Farm" },
            Tags: tags ?? new[] { "family" },
            Category: category,
            PubDate: "2026-04-29");

    [Fact]
    public void ComposeSummary_IncludesTitleDescriptionAiSummaryTakeawaysFaqMentions()
    {
        var text = PostTextComposer.ComposeSummary(SamplePost());

        Assert.Contains("Welcome", text);
        Assert.Contains("An intro post.", text);
        Assert.Contains("AI Summary: AI summary text", text);
        Assert.Contains("- One", text);
        Assert.Contains("- Two", text);
        Assert.Contains("Q: Q1?", text);
        Assert.Contains("A: A1.", text);
        Assert.Contains("Mentions: Kingdom Farm", text);
    }

    [Fact]
    public void ComposeSummary_OmitsAiSummary_WhenNull()
    {
        var text = PostTextComposer.ComposeSummary(SamplePost(aiSummary: null));
        Assert.DoesNotContain("AI Summary:", text);
    }

    [Fact]
    public void ComposeSummary_OmitsFaqSection_WhenEmpty()
    {
        var text = PostTextComposer.ComposeSummary(SamplePost(faq: Array.Empty<FaqEntry>()));
        Assert.DoesNotContain("FAQ:", text);
        Assert.DoesNotContain("Q:", text);
    }

    [Fact]
    public void ComposeSummary_OmitsTakeawaysSection_WhenEmpty()
    {
        var text = PostTextComposer.ComposeSummary(SamplePost(takeaways: Array.Empty<string>()));
        Assert.DoesNotContain("Key Takeaways:", text);
    }

    [Fact]
    public void ComposeBody_IncludesTitleDescriptionTagsCategoryMentionsBody()
    {
        var text = PostTextComposer.ComposeBody(SamplePost(
            tags: new[] { "family", "faith" },
            category: "Reflections",
            mentions: new[] { "Florida" }));

        Assert.Contains("Welcome", text);
        Assert.Contains("An intro post.", text);
        Assert.Contains("Tags: family, faith", text);
        Assert.Contains("Category: Reflections", text);
        Assert.Contains("Mentions: Florida", text);
        Assert.Contains("Hello from the road.", text);
    }

    [Fact]
    public void ComposeBody_OmitsCategoryAndTags_WhenAbsent()
    {
        var text = PostTextComposer.ComposeBody(SamplePost(
            tags: Array.Empty<string>(),
            category: null));
        Assert.DoesNotContain("Tags:", text);
        Assert.DoesNotContain("Category:", text);
    }

    [Fact]
    public void ComposeBody_NeverEmitsLabelFollowedByNothing()
    {
        var text = PostTextComposer.ComposeBody(SamplePost(
            mentions: Array.Empty<string>()));
        Assert.DoesNotMatch(@"Mentions:\s*\n", text);
    }
}
