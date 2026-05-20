using Darbee.Gateway.Domain.Models;

namespace Darbee.Gateway.Tests.Memory;

public class MemoryKindTests
{
    [Fact]
    public void ForKind_Post_ReturnsMemoryPostsCollectionName()
    {
        Assert.Equal("memory_posts", MemoryCollections.ForKind(MemoryKind.Post));
    }

    [Fact]
    public void Meta_CollectionConstant_IsMemoryMeta()
    {
        Assert.Equal("memory_meta", MemoryCollections.Meta);
    }

    [Fact]
    public void Posts_CollectionConstant_IsMemoryPosts()
    {
        Assert.Equal("memory_posts", MemoryCollections.Posts);
    }
}
