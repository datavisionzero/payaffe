using Microsoft.Extensions.Options;

namespace Payaffe.Mcp;

/// <summary>
/// Fixed-window abuse protection for MCP write tools. The host is a single
/// local process, so an in-process window is sufficient.
/// </summary>
public sealed class AdminMcpRateLimiter(IOptions<AdminMcpOptions> options)
{
    private readonly Lock _gate = new();

    private DateTimeOffset _windowStartedAt = DateTimeOffset.MinValue;
    private int _count;

    public bool TryAcquire(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_windowStartedAt == DateTimeOffset.MinValue || now - _windowStartedAt >= options.Value.Window)
            {
                _windowStartedAt = now;
                _count = 0;
            }

            if (_count >= options.Value.PermitLimit)
            {
                return false;
            }

            _count++;
            return true;
        }
    }
}
