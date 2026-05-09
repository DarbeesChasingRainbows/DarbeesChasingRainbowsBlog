using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Darbee.Gateway.Plugins;
using Darbee.Gateway.Middleware;
using Darbee.Gateway.Hubs;

namespace Darbee.Gateway;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 1. Add Services
        builder.Services.AddSignalR();

        // Configuration
        var obsidianVault = builder.Configuration["Obsidian:VaultPath"] ?? throw new Exception("Missing Obsidian Vault Path");
        var lmStudioUrl = builder.Configuration["AI:LMStudioUrl"] ?? "http://localhost:1234/v1";
        var modelId = builder.Configuration["AI:ModelId"] ?? "local-model";

        var arangoUrl = builder.Configuration["ArangoDB:Url"] ?? "http://localhost:8529";
        var arangoDb = builder.Configuration["ArangoDB:Database"] ?? "darbees_knowledge";
        var arangoUser = builder.Configuration["ArangoDB:User"] ?? "root";
        var arangoPass = builder.Configuration["ArangoDB:Password"] ?? "password";

        var cfAccountId = builder.Configuration["Cloudflare:AccountId"] ?? "YOUR_ID";
        var cfToken = builder.Configuration["Cloudflare:ApiToken"] ?? "YOUR_TOKEN";

        // Validate Context7 endpoint at boot — fail fast rather than on first user query.
        var ctx7Endpoint = builder.Configuration["Context7:Endpoint"];
        if (string.IsNullOrWhiteSpace(ctx7Endpoint))
        {
            throw new InvalidOperationException(
                "Context7:Endpoint configuration is required (e.g., 'http://localhost:3000/mcp').");
        }

        // 2. Register the MCP tool client used by ResearchPlugin (admin kernel only).
        builder.Services.AddSingleton<IMcpToolClient>(_ => new Context7McpToolClient(ctx7Endpoint));

        // 3. Two-kernel registration enforces the sovereign trust boundary:
        //    - kernel-kidsafe: surfaced to KidSafeHub (children's chat). Local-only plugins.
        //      Excludes ResearchPlugin because LLM-controlled libraryName/query strings would
        //      egress to Context7 with no policy enforcement.
        //      NOTE: AssetPlugin (Cloudflare egress) should be re-evaluated under the same
        //      threat model in a follow-up; conservatively excluded from kernel-kidsafe for now.
        //    - kernel-admin: surfaced to ParentHub and any future admin/management surfaces.
        //      Includes all plugins, including ResearchPlugin and AssetPlugin.
        builder.Services.AddKeyedSingleton<Kernel>("kernel-kidsafe", (sp, _) =>
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(modelId, lmStudioUrl);

            kernelBuilder.Plugins.AddFromObject(new ObsidianPlugin(obsidianVault), "Obsidian");
            kernelBuilder.Plugins.AddFromObject(new ArangoPlugin(arangoUrl, arangoDb, arangoUser, arangoPass), "ArangoDB");
            kernelBuilder.Plugins.AddFromObject(new GEOPlugin(lmStudioUrl, modelId), "GEO");
            kernelBuilder.Plugins.AddFromObject(new GitPlugin(), "Git");
            // Intentionally NOT registered on kernel-kidsafe: AssetPlugin, ResearchPlugin.

            return kernelBuilder.Build();
        });

        builder.Services.AddKeyedSingleton<Kernel>("kernel-admin", (sp, _) =>
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(modelId, lmStudioUrl);

            kernelBuilder.Plugins.AddFromObject(new ObsidianPlugin(obsidianVault), "Obsidian");
            kernelBuilder.Plugins.AddFromObject(new ArangoPlugin(arangoUrl, arangoDb, arangoUser, arangoPass), "ArangoDB");
            kernelBuilder.Plugins.AddFromObject(new AssetPlugin(cfAccountId, cfToken), "Assets");
            kernelBuilder.Plugins.AddFromObject(new GEOPlugin(lmStudioUrl, modelId), "GEO");
            kernelBuilder.Plugins.AddFromObject(new GitPlugin(), "Git");
            kernelBuilder.Plugins.AddFromObject(new ResearchPlugin(sp.GetRequiredService<IMcpToolClient>()), "Research");

            return kernelBuilder.Build();
        });

        var app = builder.Build();

        // 4. Configure Middleware & Endpoints
        app.UseMiddleware<SafetyMiddleware>();

        app.MapGet("/", () => "Darbee Sovereign AI Gateway Active");

        app.MapHub<KidSafeHub>("/hubs/kidsafe");
        app.MapHub<ParentHub>("/hubs/parent");

        Console.WriteLine("🚀 Darbee Sovereign Gateway Initializing...");
        app.Run();
    }
}
