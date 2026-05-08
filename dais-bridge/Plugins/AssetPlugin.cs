using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace DAIS.Bridge.Plugins;

public class AssetPlugin
{
    private readonly string _accountId;
    private readonly string _apiToken;

    public AssetPlugin(string accountId, string apiToken)
    {
        _accountId = accountId;
        _apiToken = apiToken;
    }

    [KernelFunction, Description("Uploads a local image to Cloudflare and returns permanent URL.")]
    public async Task<string> WashImage(string localPath)
    {
        // Placeholder for Cloudflare Upload API
        return $"https://imagedelivery.net/{_accountId}/test-image/public";
    }
}
