using System.Collections.Generic;

namespace Darbee.Gateway.Models;

public class SafetyPolicy
{
    public List<string> BlockedKeywords { get; set; } = new();
    public string RefusalMessage { get; set; } = "Request contains unsafe content and has been blocked.";
}
