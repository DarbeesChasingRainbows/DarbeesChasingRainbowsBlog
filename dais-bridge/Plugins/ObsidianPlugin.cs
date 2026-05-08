using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Darbee.Gateway.Plugins;

public class ObsidianPlugin
{
    private readonly string _vaultPath;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ISerializer _yamlSerializer;

    public ObsidianPlugin(string vaultPath)
    {
        _vaultPath = vaultPath;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    [KernelFunction, Description("Reads an Obsidian note and returns its full content.")]
    public async Task<string> GetNoteContent(string slug)
    {
        var path = Path.Combine(_vaultPath, $"{slug}.md");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Note not found: {path}");
        }

        return await File.ReadAllTextAsync(path);
    }

    [KernelFunction, Description("Updates the YAML frontmatter of an Obsidian note.")]
    public async Task UpdateNoteMetadata(string slug, string yamlContent)
    {
        var path = Path.Combine(_vaultPath, $"{slug}.md");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Note not found: {path}");
        }

        var fullContent = await File.ReadAllTextAsync(path);
        
        string body;
        if (fullContent.StartsWith("---"))
        {
            var endOfFrontmatter = fullContent.IndexOf("---", 3);
            if (endOfFrontmatter == -1)
            {
                body = string.Empty;
            }
            else
            {
                body = fullContent.Substring(endOfFrontmatter + 3).TrimStart();
            }
        }
        else
        {
            body = fullContent;
        }

        // Robust parsing of the provided YAML block
        var metadata = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);
        var serializedMetadata = _yamlSerializer.Serialize(metadata);

        var newContent = $"---\n{serializedMetadata.Trim()}\n---\n\n{body}";
        await File.WriteAllTextAsync(path, newContent);
    }
}
