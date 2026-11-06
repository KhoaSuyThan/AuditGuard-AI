using System.Threading;
using System.Threading.Tasks;
using AuditGuard.Application.Models;

namespace AuditGuard.Application.Interfaces;

/// <summary>
/// Giao diện hàng đợi công việc đối soát bất đồng bộ (Thread-safe Channel)
/// </summary>
public interface IAuditTaskQueue
{
    ValueTask EnqueueAsync(AuditBackgroundJob job, CancellationToken cancellationToken = default);
    ValueTask<AuditBackgroundJob> DequeueAsync(CancellationToken cancellationToken = default);
}
