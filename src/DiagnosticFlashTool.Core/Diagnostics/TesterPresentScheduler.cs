namespace DiagnosticFlashTool.Core.Diagnostics;

/// <summary>
/// Periodically sends a suppressed-positive-response Tester Present (3E 80)
/// while a non-default diagnostic session is active.
/// </summary>
public sealed class TesterPresentScheduler : IAsyncDisposable
{
    private readonly UdsClient _udsClient;
    private readonly UdsAddressing _addressing;
    private readonly TimeSpan _interval;
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public TesterPresentScheduler(
        UdsClient udsClient,
        TimeSpan interval,
        UdsAddressing addressing = UdsAddressing.Functional)
    {
        _udsClient = udsClient ?? throw new ArgumentNullException(nameof(udsClient));
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Tester Present interval must be greater than zero.");
        }

        _interval = interval;
        _addressing = addressing;
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _loop is { IsCompleted: false };
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_loop is { IsCompleted: false })
            {
                return;
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _loop = RunAsync(_cts.Token);
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_sync)
        {
            if (_cts is null || _loop is null)
            {
                return;
            }

            cts = _cts;
            cts.Cancel();
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _udsClient.SendNoResponseAsync(
                    0x3E,
                    [0x80],
                    _addressing,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
