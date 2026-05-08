using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace Darbee.Gateway.Plugins;

public class GitPlugin
{
    [KernelFunction, Description("Stages and commits a specific file with a message.")]
    public async Task StageAndCommit(string filePath, string message)
    {
        Console.WriteLine($"📝 Committing {filePath} with message: {message}");
        
        await RunGitCommand($"add \"{filePath}\"");
        await RunGitCommand($"commit -m \"{message}\"");
    }

    [KernelFunction, Description("Pushes all local commits to the remote repository.")]
    public async Task PushChanges()
    {
        Console.WriteLine("⬆️ Pushing changes to remote...");
        await RunGitCommand("push");
    }

    private async Task RunGitCommand(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) throw new Exception("Failed to start git process.");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            // In a real app, we might want to handle "nothing to commit" gracefully
            Console.WriteLine($"⚠️ Git warning/error: {error}");
        }
    }
}
