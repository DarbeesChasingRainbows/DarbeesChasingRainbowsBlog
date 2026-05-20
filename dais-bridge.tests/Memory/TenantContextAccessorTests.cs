using Darbee.Gateway.Models;

namespace Darbee.Gateway.Tests.Memory;

public class TenantContextAccessorTests
{
    [Fact]
    public void Current_DefaultsToNull()
    {
        ITenantContextAccessor acc = new TenantContextAccessor();
        Assert.Null(acc.Current);
    }

    [Fact]
    public void Required_ThrowsWhenNotSet()
    {
        ITenantContextAccessor acc = new TenantContextAccessor();
        Assert.Throws<InvalidOperationException>(() => acc.Required);
    }

    [Fact]
    public async Task AsyncLocal_FlowsAcrossAwait()
    {
        ITenantContextAccessor acc = new TenantContextAccessor();
        acc.Current = TenantContext.ForKid("lila");
        await Task.Yield();
        Assert.Equal("kid:lila", acc.Required.TenantId.Value);
    }

    [Fact]
    public async Task AsyncLocal_IsolatesParallelTasks()
    {
        ITenantContextAccessor acc = new TenantContextAccessor();
        var t1 = Task.Run(async () =>
        {
            acc.Current = TenantContext.ForKid("a");
            await Task.Delay(20);
            return acc.Required.TenantId.Value;
        });
        var t2 = Task.Run(async () =>
        {
            acc.Current = TenantContext.ForKid("b");
            await Task.Delay(20);
            return acc.Required.TenantId.Value;
        });
        var ids = await Task.WhenAll(t1, t2);
        Assert.Contains("kid:a", ids);
        Assert.Contains("kid:b", ids);
    }
}
