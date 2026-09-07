namespace StoreExpiryInspector.Application.Updates;

public sealed class UpdateCheckRuntime : IDisposable
{
    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _check;
    private readonly Action<UpdateCheckResult> _completed;
    private readonly Action<string>? _diagnostic;
    private readonly TimeSpan? _interval;
    private readonly CancellationTokenSource _cancellation = new();
    private int _started;
    private bool _disposed;
    private int _disposeStarted;

    public UpdateCheckRuntime(Func<CancellationToken, Task<UpdateCheckResult>> check, Action<UpdateCheckResult> completed, Action<string>? diagnostic = null, TimeSpan? interval = null)
    {
        _check = check;
        _completed = completed;
        _diagnostic = diagnostic;
        _interval = interval;
        _diagnostic?.Invoke("check-cts-created");
    }

    public void Start()
    {
        StartAfter(Task.CompletedTask);
    }

    public void StartAfter(Task coreReady)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _diagnostic?.Invoke("check-started");
        _ = RunAsync(coreReady);
    }

    private async Task RunAsync(Task coreReady)
    {
        try
        {
            await coreReady.WaitAsync(_cancellation.Token);
            do
            {
                var result = await _check(_cancellation.Token);
                if (!_disposed && !_cancellation.IsCancellationRequested) _completed(result);
                if (_interval is null) break;
                await Task.Delay(_interval.Value, _cancellation.Token);
            } while (!_disposed && !_cancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        _disposed = true;
        _diagnostic?.Invoke("check-cts-cancelled");
        _cancellation.Cancel();
        _cancellation.Dispose();
        _diagnostic?.Invoke("check-cts-disposed");
    }
}
