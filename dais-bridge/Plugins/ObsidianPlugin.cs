using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace DAIS.Bridge.Plugins;

public class ObsidianPlugin
{
    private readonly string _vaultPath;

    public ObsidianPlugin(string vaultPath)
    {
        _vaultPath = vaultPath;
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
        
        // Simple frontmatter parser
        string body;
        if (fullContent.StartsWith("---"))
        {
            var endOfFrontmatter = fullContent.IndexOf("---", 3);
            if (endOfFrontmatter == -1)
            {
                body = fullContent;
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

        var newContent = $"---\n{yamlContent.Trim()}\n---\n\n{body}";
        await File.WriteAllTextAsync(path, newContent);
    }
}
