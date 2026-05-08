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

        // 2. Setup Semantic Kernel and Plugins as Services
        builder.Services.AddSingleton<Kernel>(_ => 
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(modelId, lmStudioUrl);
            
            kernelBuilder.Plugins.AddFromObject(new ObsidianPlugin(obsidianVault), "Obsidian");
            kernelBuilder.Plugins.AddFromObject(new ArangoPlugin(arangoUrl, arangoDb, arangoUser, arangoPass), "ArangoDB");
            kernelBuilder.Plugins.AddFromObject(new AssetPlugin(cfAccountId, cfToken), "Assets");
            kernelBuilder.Plugins.AddFromObject(new GEOPlugin(lmStudioUrl, modelId), "GEO");
            kernelBuilder.Plugins.AddFromObject(new GitPlugin(), "Git");

            return kernelBuilder.Build();
        });

        var app = builder.Build();

        // 3. Configure Middleware & Endpoints
        app.UseMiddleware<SafetyMiddleware>();

        app.MapGet("/", () => "Darbee Sovereign AI Gateway Active");
        
        app.MapHub<KidSafeHub>("/hubs/kidsafe");
        app.MapHub<ParentHub>("/hubs/parent");

        Console.WriteLine("🚀 Darbee Sovereign Gateway Initializing...");
        app.Run();
    }
}
