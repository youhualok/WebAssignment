using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WebAssignment.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        public async Task SendMessage(string message)
        {
            message = (message ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
            {
                return;
            }

            string userName =
                Context.User?.Identity?.Name ?? "Unknown User";

            string time = DateTime.Now.ToString("hh:mm tt");

            await Clients.All.SendAsync(
                "ReceiveMessage",
                userName,
                message,
                time
            );
        }
    }
}