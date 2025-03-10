using GuerrillaNtp;

namespace ToyDbRouting.Models;

/// <summary>
/// Uses Network Time Protocol (NTP) servers to ensure that time is synchronized across clients.
/// This ensures time is consistent and correctly ordered even if local time is skewed.
/// </summary>
public sealed class NtpClock : IDisposable
{
    private readonly Timer _timer;
    private TimeSpan _correction;

    public NtpClock()
    {
        // TODO: convert this into a background service
        // Updates the correction offset every minute to avoid having to call NTP servers for each request
        _timer = new Timer(UpdateCorrection, null, 0, 60000);
    }

    public DateTime Now
    {
        get { return DateTime.UtcNow + _correction; }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void UpdateCorrection(object? state)
    {
        _correction = NtpClient.Default.Query().CorrectionOffset;
    }
}
