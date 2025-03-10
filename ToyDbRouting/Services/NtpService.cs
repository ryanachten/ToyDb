using GuerrillaNtp;

namespace ToyDbRouting.Services;

public sealed class NtpService : INtpService, IDisposable
{
    private readonly Timer _timer;
    private TimeSpan _correction;

    public NtpService()
    {
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
