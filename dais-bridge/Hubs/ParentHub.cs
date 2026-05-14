using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Darbee.Gateway.Hubs
{
    /// <summary>
    /// Hub for the Parent Dashboard, facilitating real-time monitoring and approvals.
    /// </summary>
    public class ParentHub : Hub
    {
        private readonly ILogger<ParentHub> _logger;

        public ParentHub(ILogger<ParentHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Sends a real-time alert to the parent dashboard.
        /// </summary>
        public async Task SendAlert(string alertType, string details)
        {
            _logger.LogWarning("ParentHub: Alert triggered - {Type}: {Details}", alertType, details);
            await Clients.All.SendAsync("ReceiveAlert", alertType, details);
        }

        /// <summary>
        /// Sends a request for parental approval.
        /// </summary>
        public async Task RequestApproval(string requestId, string description)
        {
            _logger.LogInformation("ParentHub: Approval requested for {RequestId}: {Description}", requestId, description);
            await Clients.All.SendAsync("ApprovalRequired", requestId, description);
        }
    }
}
