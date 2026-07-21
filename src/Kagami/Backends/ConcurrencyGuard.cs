using System.Diagnostics;
using System.Text.Json;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Backends;

/// <summary>
/// Thread-safe mutex for preventing concurrent input operations.
/// Uses a named system mutex scoped to the user session.
/// </summary>
public class ConcurrencyGuard : IDisposable
{
    private readonly Mutex? _mutex;

    public ConcurrencyGuard()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var name = $"Global\\Kagami-{sessionId}";
        _mutex = new Mutex(false, name);
    }

    public bool TryAcquire(int timeoutMs = 0)
    {
        try
        {
            return _mutex?.WaitOne(timeoutMs) ?? true;
        }
        catch (AbandonedMutexException)
        {
            // Previous holder crashed — we now own it
            return true;
        }
    }

    public void Release()
    {
        try { _mutex?.ReleaseMutex(); }
        catch { }
    }

    public void Dispose()
    {
        Release();
        _mutex?.Dispose();
    }
}
