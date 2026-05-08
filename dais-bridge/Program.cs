using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using DAIS.Bridge.Plugins;

namespace DAIS.Bridge;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 DAIS Bridge Initializing...");

        // 1. Load Configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var obsidianVault = configuration["Obsidian:VaultPath"] ?? throw new Exception("Missing Obsidian Vault Path");
        var lmStudioUrl = configuration["AI:LMStudioUrl"] ?? "http://localhost:1234/v1";
        var modelId = configuration["AI:ModelId"] ?? "local-model";
        
        var arangoUrl = configuration["ArangoDB:Url"] ?? "http://localhost:8529";
        var arangoDb = configuration["ArangoDB:Database"] ?? "darbees_knowledge";
        var arangoUser = configuration["ArangoDB:User"] ?? "root";
        var arangoPass = configuration["ArangoDB:Password"] ?? "password";

        var cfAccountId = configuration["Cloudflare:AccountId"] ?? "YOUR_ID";
        var cfToken = configuration["Cloudflare:ApiToken"] ?? "YOUR_TOKEN";

        // 2. Setup Semantic Kernel
        var builder = Kernel.CreateBuilder();
        
        // Using OpenAI connector for local LM Studio (OpenAI-compatible)
        builder.AddOpenAIChatCompletion(modelId, lmStudioUrl);

        // 3. Register Plugins
        builder.Plugins.AddFromObject(new ObsidianPlugin(obsidianVault), "Obsidian");
        builder.Plugins.AddFromObject(new ArangoPlugin(arangoUrl, arangoDb, arangoUser, arangoPass), "ArangoDB");
        builder.Plugins.AddFromObject(new AssetPlugin(cfAccountId, cfToken), "Assets");
        builder.Plugins.AddFromObject(new GEOPlugin(lmStudioUrl, modelId), "GEO");
        builder.Plugins.AddFromObject(new GitPlugin(), "Git");

        var kernel = builder.Build();

        // 4. Configure Automatic Function Calling
        OpenAIPromptExecutionSettings settings = new() 
        { 
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() 
        };

        Console.WriteLine("✨ DAIS Bridge ready. Starting Semantic Loop...");

        // Example trigger (in production this would be a file watcher or scheduled task)
        string prompt = "Find all Obsidian notes with status '✨ Process', run the GEO and asset pipelines, emit MDX, and commit/push each file.";
        
        try 
        {
            // Note: In this scaffolding phase, we're just verifying the wiring.
            // The actual processing logic will be refined in Task 6+.
            // await kernel.InvokePromptAsync(prompt, new(settings));
            Console.WriteLine("✅ Semantic Orchestration wired and ready.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error during orchestration: {ex.Message}");
        }

        Console.WriteLine("👋 Shutdown.");
    }
}
