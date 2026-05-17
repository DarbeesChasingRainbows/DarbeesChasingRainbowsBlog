using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Darbee.Gateway.Plugins;
using Darbee.Gateway.Middleware;
using Darbee.Gateway.Hubs;
using Darbee.Gateway.Memory;
using Darbee.Gateway.Models;
using Darbee.Gateway.Endpoints;

namespace Darbee.Gateway;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 1. Add Services
        builder.Services.AddSignalR();

        // Configuration
        var obsidianVault = builder.Configuration["Obsidian:VaultPath"] ?? throw new Exception("Missing Obsidian Vault Path");
        var legacyLmStudioUrl = Environment.GetEnvironmentVariable("LMSTUDIO_URL");
        var lmChatUrl = Environment.GetEnvironmentVariable("LLM_CHAT_URL")
            ?? legacyLmStudioUrl
            ?? builder.Configuration["AI:ChatUrl"]
            ?? "http://localhost:8080/v1";
        if (legacyLmStudioUrl is not null && Environment.GetEnvironmentVariable("LLM_CHAT_URL") is null)
        {
            Console.WriteLine("[bridge] LMSTUDIO_URL is deprecated; rename to LLM_CHAT_URL in .env / compose.yaml.");
        }

        var lmEmbeddingUrl = Environment.GetEnvironmentVariable("LLM_EMBEDDING_URL")
            ?? builder.Configuration["AI:EmbeddingUrl"]
            ?? lmChatUrl;

        var modelId = Environment.GetEnvironmentVariable("AI_MODEL_ID")
            ?? builder.Configuration["AI:ModelId"]
            ?? "llama-4-maverick";

        var arangoUrl = Environment.GetEnvironmentVariable("ARANGO_URL")
            ?? builder.Configuration["ArangoDB:Url"]
            ?? "http://localhost:8529";
        var arangoDb = Environment.GetEnvironmentVariable("ARANGO_DATABASE")
            ?? builder.Configuration["ArangoDB:Database"]
            ?? "darbees_knowledge";
        var arangoUser = Environment.GetEnvironmentVariable("ARANGO_USER")
            ?? builder.Configuration["ArangoDB:User"]
            ?? "root";
        var arangoPass = Environment.GetEnvironmentVariable("ARANGO_PASSWORD")
            ?? builder.Configuration["ArangoDB:Password"]
            ?? "password";

        var embeddingModelId = Environment.GetEnvironmentVariable("AI_EMBEDDING_MODEL_ID")
            ?? builder.Configuration["AI:EmbeddingModelId"]
            ?? "qwen3-embedding-8b";
        var embeddingDimension = int.Parse(
            Environment.GetEnvironmentVariable("AI_EMBEDDING_DIMENSION")
            ?? builder.Configuration["AI:EmbeddingDimension"]
            ?? "4096");
        var vectorNLists = int.Parse(builder.Configuration["Memory:VectorNLists"] ?? "100");

        var lmApiKey = Environment.GetEnvironmentVariable("AI_API_KEY")
            ?? Environment.GetEnvironmentVariable("LMSTUDIO_API_KEY")
            ?? builder.Configuration["AI:ApiKey"];

        builder.Services.AddHttpClient("memory");
        builder.Services.AddSingleton<IEmbeddingClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("memory");
            return new OpenAiCompatibleEmbeddingClient(http, lmEmbeddingUrl, embeddingModelId, embeddingDimension, lmApiKey);
        });
        builder.Services.AddSingleton<MemoryStore>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("memory");
            return new MemoryStore(arangoUrl, arangoDb, arangoUser, arangoPass, embeddingModelId, embeddingDimension, vectorNLists, http, sp.GetRequiredService<IEmbeddingClient>());
        });
        builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();

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
            kernelBuilder.AddOpenAIChatCompletion(modelId, lmChatUrl);

            kernelBuilder.Plugins.AddFromObject(new ObsidianPlugin(obsidianVault), "Obsidian");
            kernelBuilder.Plugins.AddFromObject(new MemoryPlugin(sp.GetRequiredService<MemoryStore>(), sp.GetRequiredService<ITenantContextAccessor>()), "Memory");
            kernelBuilder.Plugins.AddFromObject(new GEOPlugin(lmChatUrl, modelId), "GEO");
            kernelBuilder.Plugins.AddFromObject(new GitPlugin(), "Git");
            // Intentionally NOT registered on kernel-kidsafe: AssetPlugin, ResearchPlugin.

            return kernelBuilder.Build();
        });

        builder.Services.AddKeyedSingleton<Kernel>("kernel-admin", (sp, _) =>
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(modelId, lmChatUrl);

            kernelBuilder.Plugins.AddFromObject(new ObsidianPlugin(obsidianVault), "Obsidian");
            kernelBuilder.Plugins.AddFromObject(new MemoryPlugin(sp.GetRequiredService<MemoryStore>(), sp.GetRequiredService<ITenantContextAccessor>()), "Memory");
            kernelBuilder.Plugins.AddFromObject(new AssetPlugin(cfAccountId, cfToken), "Assets");
            kernelBuilder.Plugins.AddFromObject(new GEOPlugin(lmChatUrl, modelId), "GEO");
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

        app.MapPost("/api/admin/reindex-posts", async (
            ReindexRequest request,
            MemoryStore store,
            IEmbeddingClient embeddings,
            CancellationToken ct) =>
        {
            try
            {
                var response = await ContentRagEndpoints.HandleReindexAsync(request, store, embeddings, ct);
                return Results.Ok(response);
            }
            catch (EmbeddingConfigMismatchException ex)
            {
                return Results.Json(new
                {
                    error = "embedding_config_mismatch",
                    message = ex.Message,
                    previous = ex.Previous,
                    current = ex.Current,
                }, statusCode: 503);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = "invalid_request", details = ex.Message });
            }
        });

        app.MapPost("/api/memory/search", async (
            SearchRequest request,
            MemoryStore store,
            IEmbeddingClient embeddings,
            CancellationToken ct) =>
        {
            try
            {
                var response = await ContentRagEndpoints.HandleSearchAsync(request, store, embeddings, ct);
                return Results.Ok(response);
            }
            catch (EmbeddingConfigMismatchException ex)
            {
                return Results.Json(new { error = "embedding_config_mismatch", message = ex.Message },
                    statusCode: 503);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = "invalid_request", details = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(new { error = "embedding_server_unreachable", message = ex.Message },
                    statusCode: 503);
            }
        });

        app.MapPost("/api/admin/migrate-embeddings", async (
            MigrateRequest request,
            MemoryStore store,
            CancellationToken ct) =>
        {
            try
            {
                var result = await ContentRagEndpoints.HandleMigrateAsync(request, store, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new
                {
                    error = "missing_or_invalid_confirm",
                    message = ex.Message,
                    accepted = new[] { "preserve-and-reembed", "wipe-and-reset" }
                });
            }
        });

        Console.WriteLine("🚀 Darbee Sovereign Gateway Initializing...");
        app.Run();
    }
}
