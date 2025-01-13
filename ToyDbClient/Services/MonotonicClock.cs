using GuerrillaNtp;

namespace ToyDbClient.Services;

/// <summary>
/// Uses Network Time Protocol (NTP) servers to ensure that time is synchronized across clients.
/// This ensures time is monotonic and correctly order even if local time is skewed.
/// </summary>
public sealed class MonotonicClock : IDisposable
{
    private readonly Timer _timer;
    private TimeSpan _correction;

    public MonotonicClock()
    {
        // Updates the correction offset every minute to avoid having to call NTP servers for each request
        _timer = new Timer(UpdateCorrection, null, 0, 60000);
    }

    public DateTime GetMonotonicNow() => DateTime.UtcNow + _correction;

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void UpdateCorrection(object? state)
    {
        _correction = NtpClient.Default.Query().CorrectionOffset;
    }
}
