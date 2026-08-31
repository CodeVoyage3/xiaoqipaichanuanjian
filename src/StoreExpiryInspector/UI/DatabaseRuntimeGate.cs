namespace StoreExpiryInspector.UI;

public static class DatabaseRuntimeGate
{
    private static readonly object Sync = new();
    private static bool _maintenance;
    private static int _activeOperations;
    private static TaskCompletionSource<object?>? _idle;

    public static bool IsMaintenance
    {
        get
        {
            lock (Sync)
            {
                return _maintenance;
            }
        }
    }

    public static int ActiveOperations
    {
        get
        {
            lock (Sync)
            {
                return _activeOperations;
            }
        }
    }

    public static T Run<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnterOperation();
        try
        {
            return operation();
        }
        finally
        {
            ExitOperation();
        }
    }

    public static async Task<IDisposable?> EnterMaintenanceAsync()
    {
        Task waitTask;
        lock (Sync)
        {
            if (_maintenance)
            {
                return null;
            }

            _maintenance = true;
            if (_activeOperations == 0)
            {
                return new MaintenanceLease();
            }

            _idle = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            waitTask = _idle.Task;
        }

        await waitTask.ConfigureAwait(true);
        return new MaintenanceLease();
    }

    private static void EnterOperation()
    {
        lock (Sync)
        {
            if (_maintenance)
            {
                throw new DatabaseRuntimeStoppedException();
            }

            _activeOperations++;
        }
    }

    private static void ExitOperation()
    {
        lock (Sync)
        {
            _activeOperations--;
            if (_activeOperations == 0 && _maintenance)
            {
                _idle?.TrySetResult(null);
                _idle = null;
            }
        }
    }

    private static void ExitMaintenance()
    {
        lock (Sync)
        {
            _maintenance = false;
            _idle = null;
        }
    }

    private sealed class MaintenanceLease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ExitMaintenance();
            }
        }
    }
}

public sealed class DatabaseRuntimeStoppedException : InvalidOperationException
{
    public DatabaseRuntimeStoppedException()
        : base("数据库 runtime 当前已暂停。")
    {
    }
}
