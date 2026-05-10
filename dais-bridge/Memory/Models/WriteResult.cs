namespace Darbee.Gateway.Memory.Models;

public record WriteResult(string Id, bool Completed, bool Queued)
{
    public static WriteResult Ready(string id) => new(id, Completed: true, Queued: false);
    public static WriteResult Pending(string id) => new(id, Completed: false, Queued: true);
}
