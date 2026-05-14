using System.IO;
using System.Text.Json;
using Xunit;

namespace DAIS.Bridge.Tests;

public class ScaffoldingTests
{
    [Fact]
    public void AppSettings_ShouldExistAndHaveRequiredSections()
    {
        // Arrange - find the project root by looking up from the assembly
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "dais-bridge.tests.csproj")))
        {
            currentDir = currentDir.Parent;
        }
        
        Assert.NotNull(currentDir);
        var projectRoot = currentDir.Parent; 
        
        Assert.NotNull(projectRoot);
        var appSettingsPath = Path.Combine(projectRoot.FullName, "dais-bridge", "appsettings.json");

        // Act & Assert
        Assert.True(File.Exists(appSettingsPath), $"appsettings.json should exist at {appSettingsPath}");

        var json = File.ReadAllText(appSettingsPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("Obsidian", out _), "Missing 'Obsidian' section");
        Assert.True(root.TryGetProperty("ArangoDB", out _), "Missing 'ArangoDB' section");
        Assert.True(root.TryGetProperty("AI", out _), "Missing 'AI' section");
        Assert.True(root.TryGetProperty("Cloudflare", out _), "Missing 'Cloudflare' section");
    }

    [Fact]
    public void AstroConfig_ShouldBeStaticOutput_WithoutUnusedAdapter()
    {
        // The site is static-output (Astro's default when `output:` is unset).
        // Cloudflare Pages serves `dist/` directly without an SSR adapter, so
        // we must NOT import `@astrojs/cloudflare` (it wasn't in package.json
        // and its import broke `astro check`).
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "astro.config.mjs")))
        {
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(currentDir);
        var astroConfigPath = Path.Combine(currentDir.FullName, "astro.config.mjs");

        var content = File.ReadAllText(astroConfigPath);

        Assert.DoesNotContain("@astrojs/cloudflare", content);
        Assert.DoesNotContain("adapter:", content);
    }

    [Fact]
    public void PackageJson_ShouldDeclareAuthoringScriptsAndDeps()
    {
        // Phase 13 authoring scripts must stay wired into package.json so the
        // npm-run entry points and their two deps don't silently disappear.
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "astro.config.mjs")))
        {
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(currentDir);
        var packageJsonPath = Path.Combine(currentDir.FullName, "package.json");
        Assert.True(File.Exists(packageJsonPath), $"package.json should exist at {packageJsonPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        var root = doc.RootElement;

        var scripts = root.GetProperty("scripts");
        foreach (var name in new[] { "geo:fill", "geo:fill:all", "related:rebuild", "image:watch", "test:scripts" })
        {
            Assert.True(scripts.TryGetProperty(name, out _), $"package.json scripts missing '{name}'");
        }

        var deps = root.GetProperty("dependencies");
        foreach (var dep in new[] { "gray-matter", "chokidar" })
        {
            Assert.True(deps.TryGetProperty(dep, out _), $"package.json dependencies missing '{dep}'");
        }
    }
}
