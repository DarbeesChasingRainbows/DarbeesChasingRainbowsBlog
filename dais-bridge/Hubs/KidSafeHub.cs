using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Darbee.Gateway.Hubs
{
    /// <summary>
    /// Hub for kid-safe communication, providing a gateway for children to interact with AI-monitored services.
    /// </summary>
    public class KidSafeHub : Hub
    {
        private readonly ILogger<KidSafeHub> _logger;

        public KidSafeHub(ILogger<KidSafeHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Receives a message from a child user and processes it through safety filters/routing.
        /// </summary>
        /// <param name="user">The name of the user sending the message.</param>
        /// <param name="message">The content of the message.</param>
        public async Task SendMessage(string user, string message)
        {
            _logger.LogInformation("KidSafeHub: Message received from {User}: {Message}", user, message);

            // TODO: Integrate with Semantic Kernel for safety filtering and response generation
            // Placeholder: For now, we simply echo back or broadcast (depending on future requirements)
            // In a real scenario, this would route to a specialized agent.

            await Clients.Caller.SendAsync("ReceiveMessage", "Gateway", $"Hi {user}! I received your message: \"{message}\". Checking if it's safe...");
        }
    }
}
