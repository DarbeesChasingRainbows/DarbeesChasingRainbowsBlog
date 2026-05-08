using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Darbee.Gateway.Models;

namespace Darbee.Gateway.Middleware;

public class SafetyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SafetyPolicy _policy;

    public SafetyMiddleware(RequestDelegate next)
    {
        _next = next;
        
        // Load policy from file
        // Note: In a production app, we might use IConfiguration or a FileWatcher
        var policyPath = Path.Combine(Directory.GetCurrentDirectory(), "safety_policies.json");
        
        // Fallback for tests or different working directories
        if (!File.Exists(policyPath))
        {
            policyPath = Path.Combine(AppContext.BaseDirectory, "safety_policies.json");
        }

        if (File.Exists(policyPath))
        {
            var json = File.ReadAllText(policyPath);
            _policy = JsonSerializer.Deserialize<SafetyPolicy>(json) ?? new SafetyPolicy();
        }
        else
        {
            _policy = new SafetyPolicy();
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.EnableBuffering();

        if (context.Request.ContentLength > 0)
        {
            using (var reader = new StreamReader(
                context.Request.Body, 
                encoding: Encoding.UTF8, 
                detectEncodingFromByteOrderMarks: false, 
                leaveOpen: true))
            {
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0; // Reset for next middleware

                foreach (var keyword in _policy.BlockedKeywords)
                {
                    if (body.Contains(keyword, System.StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync(_policy.RefusalMessage);
                        return;
                    }
                }
            }
        }

        await _next(context);
    }
}
