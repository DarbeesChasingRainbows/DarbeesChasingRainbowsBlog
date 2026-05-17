using Darbee.Gateway.Memory;
using Darbee.Gateway.Memory.Models;

namespace Darbee.Gateway.Tests.Memory;

public class EmbeddingConfigTests
{
    [Fact]
    public void EmbeddingConfig_RecordEquality_MatchesByValue()
    {
        var a = new EmbeddingConfig("qwen3-embedding-8b", 4096);
        var b = new EmbeddingConfig("qwen3-embedding-8b", 4096);
        var c = new EmbeddingConfig("nomic-embed-text-v1.5", 768);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void EmbeddingConfigMismatchException_Message_IncludesBothConfigs()
    {
        var previous = new EmbeddingConfig("nomic-embed-text-v1.5", 768);
        var current = new EmbeddingConfig("qwen3-embedding-8b", 4096);

        var ex = new EmbeddingConfigMismatchException(previous, current);

        Assert.Contains("nomic-embed-text-v1.5", ex.Message);
        Assert.Contains("768", ex.Message);
        Assert.Contains("qwen3-embedding-8b", ex.Message);
        Assert.Contains("4096", ex.Message);
        Assert.Contains("/api/admin/migrate-embeddings", ex.Message);
    }
}
