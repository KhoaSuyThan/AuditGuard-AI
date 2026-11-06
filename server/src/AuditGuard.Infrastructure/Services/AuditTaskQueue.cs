using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AuditGuard.Application.Interfaces;
using AuditGuard.Application.Models;

namespace AuditGuard.Infrastructure.Services;

/// <summary>
/// Cài đặt hàng đợi Channel có giới hạn để điều phối tác vụ nền mượt mà
/// </summary>
public class AuditTaskQueue : IAuditTaskQueue
{
    private readonly Channel<AuditBackgroundJob> _channel;

    public AuditTaskQueue(int capacity = 100)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<AuditBackgroundJob>(options);
    }

    public async ValueTask EnqueueAsync(AuditBackgroundJob job, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public async ValueTask<AuditBackgroundJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
