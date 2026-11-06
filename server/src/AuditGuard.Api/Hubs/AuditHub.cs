using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace AuditGuard.Api.Hubs;

public class AuditHub : Hub
{
    /// <summary>
    /// Cho phép client tham gia vào phòng kiểm toán theo SessionId để cách ly tiến trình
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }
}
