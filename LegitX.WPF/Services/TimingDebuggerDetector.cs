using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace LegitX.WPF.Services;

/// <summary>Layer 4 — Timing-Based Debugger Detection</summary>
internal sealed class TimingDebuggerDetector : IDisposable
{
    private System.Threading.Timer? _timer;
    private static readonly Random _jitter = new();

    // A trivial computation should finish under this threshold.
    // Single-stepping in a debugger makes it 100–1000× slower.
    private const long ThresholdMs = 200;

    // Allow N consecutive failures before triggering (avoids a single GC spike)
    private const int MaxConsecutiveFails = 3;
    private int _consecutiveFails;

    public event Action<string>? DebuggerDetected;

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool Initialize()
    {
        try
        {
            if (!TimingCheck())
            {
                _consecutiveFails++;
                if (_consecutiveFails >= MaxConsecutiveFails)
                    return false;
            }

            _timer = new System.Threading.Timer(OnTimer, null, NextInterval(), Timeout.Infinite);
            return true;
        }
        catch { return true; }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool Check() => TimingCheck();

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private bool TimingCheck()
    {
        try
        {
            var sw = Stopwatch.StartNew();

            // Block 1: arithmetic loop — should be <1ms on any modern CPU
            long acc = 0;
            for (int i = 0; i < 10_000; i++)
                acc += i * 7 + 3;

            // Block 2: small SHA-256 — should be <1ms
            Span<byte> buf = stackalloc byte[64];
            for (int i = 0; i < buf.Length; i++)
                buf[i] = (byte)(acc + i);
            SHA256.HashData(buf);

            // Block 3: Guid creation — should be <1ms
            _ = Guid.NewGuid();
            _ = Guid.NewGuid();

            sw.Stop();

            return sw.ElapsedMilliseconds < ThresholdMs;
        }
        catch { return true; }
    }

    private void OnTimer(object? state)
    {
        try
        {
            if (!TimingCheck())
            {
                _consecutiveFails++;
                if (_consecutiveFails >= MaxConsecutiveFails)
                {
                    _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                    DebuggerDetected?.Invoke("Timing anomaly — possible debugger single-stepping detected.");
                    return;
                }
            }
            else
            {
                _consecutiveFails = 0;
            }
        }
        catch { }
        finally
        {
            try { _timer?.Change(NextInterval(), Timeout.Infinite); }
            catch { }
        }
    }

    private static int NextInterval()
    {
        lock (_jitter) { return _jitter.Next(25_000, 50_001); }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
