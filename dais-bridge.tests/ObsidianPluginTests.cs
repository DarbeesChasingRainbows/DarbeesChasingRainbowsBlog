using System;
using System.IO;
using System.Threading.Tasks;
using Darbee.Gateway.Plugins;
using Xunit;

namespace DAIS.Bridge.Tests;

public class ObsidianPluginTests : IDisposable
{
    private readonly string _tempVaultPath;
    private readonly ObsidianPlugin _plugin;

    public ObsidianPluginTests()
    {
        _tempVaultPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempVaultPath);
        _plugin = new ObsidianPlugin(_tempVaultPath);
    }

    [Fact]
    public async Task GetNoteContent_ShouldReturnContent_WhenFileExists()
    {
        // Arrange
        var slug = "test-note";
        var expectedContent = "---\ntitle: Test\n---\nHello World";
        await File.WriteAllTextAsync(Path.Combine(_tempVaultPath, $"{slug}.md"), expectedContent);

        // Act
        var result = await _plugin.GetNoteContent(slug);

        // Assert
        Assert.Equal(expectedContent, result);
    }

    [Fact]
    public async Task UpdateNoteMetadata_ShouldUpdateFrontmatter_WhenFileExists()
    {
        // Arrange
        var slug = "test-update";
        var initialContent = "---\ntitle: Initial\nstatus: Draft\n---\nBody content";
        var filePath = Path.Combine(_tempVaultPath, $"{slug}.md");
        await File.WriteAllTextAsync(filePath, initialContent);
        
        var newMetadataYaml = "title: Updated\nstatus: Processed";

        // Act
        await _plugin.UpdateNoteMetadata(slug, newMetadataYaml);

        // Assert
        var result = await File.ReadAllTextAsync(filePath);
        Assert.Contains("title: Updated", result);
        Assert.Contains("status: Processed", result);
        Assert.Contains("Body content", result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultPath))
        {
            Directory.Delete(_tempVaultPath, true);
        }
    }
}
